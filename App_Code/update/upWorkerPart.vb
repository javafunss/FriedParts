﻿Imports Microsoft.VisualBasic
Imports System.Data
Imports apiOctopart

Namespace UpdateService

    '===========================================
    '== WORKER CLASS
    '===========================================

    ''' <summary>
    ''' The Update-Service Worker class for update a specific FriedPart (FPID) data.
    ''' </summary>
    ''' <remarks></remarks>
    Public Class upWorkerPart
        Inherits upProcess

        '===========================================
        '== PROPERTIES / INTERNAL STATE
        '===========================================

        ''' <summary>
        ''' The actual semaphore object used to control access. Derivative classes MUST SHADOW this 
        ''' variable in order to dissociate from the global pool of thread resources.
        ''' </summary>
        ''' <remarks></remarks>
        Protected Shared Shadows mutexSemaphore As Threading.Semaphore

        ''' <summary>
        ''' Returns the PartID of the part represented by this object
        ''' </summary>
        ''' <value>Read only!</value>
        ''' <returns>The PartID of the part being updated by this object</returns>
        ''' <remarks></remarks>
        Public ReadOnly Property GetPartID As Int32
            Get
                Return procMeta.ThreadDataID
            End Get
        End Property

        '===========================================
        '== MUTUAL EXCLUSION (MUTEX)
        '===========================================

        ''' <summary>
        ''' Initializes the mutual exclusion lock used to manage concurrency.
        ''' </summary>
        ''' <remarks>This is only really called from the constructor here. Derivative classes
        ''' should never need to mess with this, but in case I didn't forsee some additional
        ''' process specific initialization it is declared protected to allow override/extension
        ''' </remarks>
        Protected Overrides Sub MutexInit()
            If mutexSemaphore Is Nothing Then mutexSemaphore = New Threading.Semaphore(mutexMaxConcurrent, mutexMaxConcurrent)
            mutexLocked = False
        End Sub

        ''' <summary>
        ''' Attempt to gain exclusive rights to run this process.
        ''' </summary>
        ''' <returns>Whether or not we were able to successful acquire the lock.</returns>
        ''' <remarks></remarks>
        Protected Overrides Function MutexLock() As Boolean
            If mutexSemaphore.WaitOne(1) Then
                'Lock succeeded
                mutexLocked = True
                Return True
            Else
                '[Lock failed]
                'Are we deadlocked? -- happens if we exit abnormally (e.g. manually)
                Dim RunReport As New upReport
                If RunReport.NumWorkerThreads(upThreadTypes.ttWorkerPart) = 0 Then
                    'Force lock release because no one is holding it... state-sync error
                    Try
                        While True
                            'Infinite loop, we'll exit via the exception caused when we over-release it
                            MutexRelease()
                        End While
                    Catch ex As Threading.SemaphoreFullException
                        'Released all outstanding semaphores
                    End Try
                End If
                Return False
            End If
        End Function

        ''' <summary>
        ''' Releases the exclusive lock on the the right to run this process.
        ''' </summary>
        ''' <remarks>Fails silently if you call it, but there is nothing to release (because you
        ''' never locked it in the first place).</remarks>
        Protected Overrides Sub MutexRelease()
            mutexSemaphore.Release(1)
            mutexLocked = False
        End Sub

        '===========================================
        '== SPAWNING, MUTEX, & EXECUTION MECHANICS
        '===========================================

        ''' <summary>
        ''' Worker thread for the updating of parts. This 
        ''' dispatcher is separate from other sync/update processes that happen in FriedParts so that
        ''' updates can happen in parallel when sourced from different data providers. For example,
        ''' Dropbox updates and Part updates happen in parallel, but each one is throttled to a certain
        ''' rate to prevent abusing our data provider's servers.
        ''' </summary>
        ''' <returns>A human-readable message explaining what happened -- for log/display as needed (safe to ignore)</returns>
        ''' <remarks>Is called by fpusDispatch() and never directly</remarks>
        Protected Overrides Function TheActualThread() As String
            'MUTEX

            'Claim Semaphore -- LOCK!
            UpdateThreadStatus(scanStatus.scanRUNNING)

            'Find next part to update
            If GetPartID <= 0 Then
                'Find it
                procMeta.ThreadDataID = Me.NextPartToUpdate()
                'Sanity Check
                If Not fpParts.partExistsID(GetPartID) Then
                    Throw New Exception("PartID " & GetPartID & " for Part Worker was NOT Valid!")
                End If
            End If

            'Update!
            LogEvent("Scanning/Updating PartID " & GetPartID, logMsgTypes.msgSTART)
            Update()

            'Report
            UpdateThreadStatus(scanStatus.scanIDLE) 'Release LOCK
            LogEvent("Scanned/Updated PartID " & GetPartID, logMsgTypes.msgSTOP)
            Return "Scanned/Updated PartID " & GetPartID
        End Function



        '======================================
        ' PART UPDATE WORKER FUNCTIONS
        '======================================

        ''' <summary>
        ''' Updates the UpdatingPartID class state variable
        ''' Updates the current LastScanned Date/Time value in the database (Does not update the LastModified date/time -- do that only if changes are made)
        ''' [Priority One] Update any parts that have *Never* been updated.
        ''' [Priority Two] Update the part with the oldest "Last Scanned" date
        ''' </summary>
        ''' <returns>The PartID of the part to update next</returns>
        ''' <remarks>Used by the Part Update Worker thread dispatcher</remarks>
        Private Function NextPartToUpdate() As Int32
            '[Priority One] Update any parts that have *Never* been updated.
            Dim dt As New DataTable
            dbAcc.SelectRows(dt, _
                "SELECT [PartID] " & _
                "FROM [FriedParts].[dbo].[part-Common] " & _
                "WHERE [Date_LastScanned] IS NULL")
            If dt.Rows.Count > 0 Then
                Return dt.Rows(0).Field(Of Int32)("PartID")
            End If

            '[Priority Two] Update the part with the oldest "Last Scanned" date
            dt = dbAcc.SelectRows(dt, _
                "SELECT [PartID] " & _
                "FROM [FriedParts].[dbo].[part-Common] " & _
                "WHERE [Date_LastScanned] IS NOT NULL " & _
                "ORDER BY [FriedParts].[dbo].[Date_LastScanned] DESC")
            If dt.Rows.Count = 0 Then
                'Error No Parts Found! -- this can't happen
                LogEvent(sysErrors.ERR_NOTFOUND, "No Parts Found! [part-Common] table EMPTY?!")
                Return sysErrors.ERR_NOTFOUND
            Else
                'Grab the first record -- which should be the least updated one because we sorted by scan-date
                Return dt.Rows(0).Field(Of Int32)("PartID")
            End If
        End Function

        ''' <summary>
        ''' Entry point for updating a part. Checks with the data providers and updates any changed
        ''' information (for example, pricing and availability), corrects any known database data
        ''' integrity issues, and fills in any missing information. 
        ''' </summary>
        ''' <remarks>Do NOT call me faster than once per 10 seconds! (In the future, could 
        ''' optimize this by having it fork out all of the different data providers to 
        ''' different workers to run them in parallel.)</remarks>
        Public Sub Update()

            'Octopart Search
            '===============
            UpdateThreadStatus(scanStatus.scanWAITFOROP)
            Dim OP As New Octopart("The Part Number")
            'Make Changes
            'Log Changes
            'Update Status Entry in Database
            UpdateThreadStatus(scanStatus.scanIDLE)

            'Mark this one as SCANNED!
            '=========================
            Dim sqlText As String = _
                "UPDATE [FriedParts].[dbo].[part-Common]" & _
                "   SET " & _
                "      [Date_LastScanned] = " & txtSqlDate(Now) & "" & _
                "   WHERE [PartID] = " & GetPartID
            dbAcc.SQLexe(sqlText)
        End Sub



        '======================================
        ' CONSTRUCTOR
        '======================================

        ''' <summary>
        ''' Constructor. Assumes PartID is valid or will throw an ObjectNotFoundException.
        ''' </summary>
        ''' <param name="PartID">A FriedParts PartID. Must be valid.</param>
        ''' <remarks></remarks>
        Public Sub New(Optional ByVal PartID As Int32 = sysErrors.PARTADD_MFRNUMNOTUNIQUE)
            'Configure Base
            MyBase.New() 'Always do this and do it first!
            procMeta.ThreadType = upThreadTypes.ttWorkerPart

            'Perform Specifics
            If PartID > 0 Then
                'User has asked us to look into a specific PartID -- manual refresh?

                'Sanity check
                If Not fpParts.partExistsID(PartID) Then
                    Throw New Exception("The specified PartID, " & PartID & ", DOES NOT EXIST!")
                End If

                'OK to Proceed!
                procMeta.ThreadDataID = PartID
            End If
        End Sub
    End Class
End Namespace