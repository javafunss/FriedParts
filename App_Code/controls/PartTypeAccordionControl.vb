﻿Imports DevExpress.Web.ASPxGridView
Imports System.Data

Namespace System.Web.UI.FriedParts

    ''' <summary>
    ''' This implements all of the setup and operational logic of the 
    ''' PartTypeAccordion Web Server Control
    ''' </summary>
    ''' <remarks>We couldn't package this as an actual control because it couldn't emit 
    ''' the DevExpress controls on its own. So we sort of hack-tasticly make our own
    ''' loose approximate of a true server control using includes.</remarks>
    Public Class PartTypeAccordionControl

        ''' <summary>
        ''' Internal datasource.
        ''' </summary>
        ''' <remarks></remarks>
        Protected ptData As PartTypeDatasourceControl

        ''' <summary>
        ''' Contains all of the controls on the hosting webpage.
        ''' We need this in order to manipulate the controls on the host page and implement UI behavior.
        ''' </summary>
        ''' <remarks></remarks>
        Protected allControls As sysControlWalker

        ''' <summary>
        ''' Get's the currently selected PartTypeID
        ''' </summary>
        ''' <value></value>
        ''' <returns></returns>
        ''' <remarks></remarks>
        Public ReadOnly Property GetSelectedTypeID As Int32
            Get
                Return ptData.GetParentID
            End Get
        End Property

        ''' <summary>
        ''' The name of the variable in the user's session where this control 
        ''' will be stored between requests to maintain state.
        ''' </summary>
        ''' <remarks></remarks>
        Protected TheSessionStoreLocation As String

        ''' <summary>
        ''' Maximum number of user-parttype levels supported for a category.
        ''' Levels start at 2. Level 1 is "All Parts". Users may not add 
        ''' additional categories (PartTypes) at level 1.
        ''' </summary>
        ''' <remarks></remarks>
        Public Const MAX_DEPTH As Byte = 4

        ''' <summary>
        ''' Call this function everytime the page reloads (e.g. on Callbacks, Postbacks, etc...)
        ''' </summary>
        ''' <param name="SessionControlName">The control's variable name in the user's session state</param>
        ''' <param name="MePage">Pass in the "Me" argument from the calling webpage</param>
        ''' <returns></returns>
        ''' <remarks></remarks>
        Public Shared Function Reload(ByRef MePage As System.Web.UI.Page, ByRef SessionControlName As String) As PartTypeAccordionControl
            If HttpContext.Current.Session(SessionControlName) Is Nothing Then
                'Throw New NullReferenceException("The PartTypeAccordionControl must be initialized before you reload it! Use New(SessionName)")
                'Recover!
                Return New FriedParts.PartTypeAccordionControl(MePage, SessionControlName)
            Else
                Dim hPTa As PartTypeAccordionControl = HttpContext.Current.Session(SessionControlName)
                'hPTa.Update(hPTa.GetSelectedTypeID)
                Return hPTa
            End If
        End Function

        Public Sub UpdateControls(ByRef MePage As System.Web.UI.Page)
            'Collect the applicable controls
            allControls = New sysControlWalker(MePage)
            'Update the UI
            Update(ptData.GetParentID)
        End Sub

        Protected Sub Update(Optional ByVal TypeID As Integer = 0)

            DirectCast(allControls.FindControl("lbl"), Label).Text = DirectCast(allControls.FindControl("SelectedPartTypeID"), HiddenField).Value 'xxx

            'Update the user interface
            Dim ParentLevel As Byte = ptGetLevel(TypeID)
            Dim gv As ASPxGridView
            ptData.updateDatasource(TypeID)
            For i As Byte = 2 To MAX_DEPTH
                If i > ParentLevel + 1 Then
                    'Hide these!
                    DirectCast(allControls.FindControl("L" & i & "a"), HtmlGenericControl).Visible = False
                    DirectCast(allControls.FindControl("L" & i & "b"), HtmlGenericControl).Visible = False
                Else
                    'Show these!
                    DirectCast(allControls.FindControl("L" & i & "a"), HtmlGenericControl).Visible = True
                    DirectCast(allControls.FindControl("L" & i & "b"), HtmlGenericControl).Visible = True
                    'Default tab...
                    If i = ParentLevel + 1 Then
                        'This one is the one we open to...
                        DirectCast(allControls.FindControl("L" & i & "a"), HtmlGenericControl).Attributes.Add("class", "active")
                    Else
                        '...not these!
                    End If
                    'Load 'em up!
                    gv = DirectCast(allControls.FindControl("L" & i & "g"), ASPxGridView)
                    gv.DataSource = ptData.GetDatasource(i)
                    gv.DataBind()
                    DirectCast(allControls.FindControl("L" & i & "a"), HtmlGenericControl).InnerText = ptData.GetTitles(i)
                End If
            Next
            'Save in view state
            Dim hf As HiddenField = DirectCast(allControls.FindControl("SelectedPartTypeID"), HiddenField)
            hf.Value = ptData.GetParentID
        End Sub


        ' TAB HANDLERS
        '======================================
        Public Sub HandleRowChanged(ByVal WhichGrid As Object, ByVal e As System.EventArgs)
            'Get data from the grid -- what did user click on?
            Dim gv As ASPxGridView = DirectCast(WhichGrid, ASPxGridView)
            Dim hf As System.Web.UI.WebControls.HiddenField
            Dim drow As DataRow = gv.GetDataRow(DirectCast(e, DevExpress.Web.ASPxGridView.ASPxGridViewCustomCallbackEventArgs).Parameters)
            'Get the new TypeID the user has just selected
            If drow IsNot Nothing Then
                hf = DirectCast(allControls.FindControl("SelectedPartTypeID"), System.Web.UI.WebControls.HiddenField)
                hf.Value = (drow.Field(Of Int16)("TypeID"))
                'Reflect that change in the backend
                Update(hf.Value)
            End If
        End Sub

        ''' <summary>
        ''' Returns the ASPxGridViews that make up the content controls of the accordion. This plumbing is necessary to work around some 
        ''' limitations in the psuedo-control approach.  Namely, you can't ask a protected function in ...
        ''' </summary>
        ''' <returns></returns>
        ''' <remarks></remarks>
        Public Function GetGrids() As Collection
            Dim retVal As New Collection
            For i As Byte = 2 To MAX_DEPTH
                retVal.Add(DirectCast(allControls.FindControl("L" & i & "g"), ASPxGridView), "L" & i & "g")
            Next
            Return retVal
        End Function

        Public Sub Init(ByRef TheHostPage As System.Web.UI.Page)
            'Collect the applicable controls
            allControls = New sysControlWalker(TheHostPage)

            'Set Initial Values
            Dim hf As HiddenField = DirectCast(allControls.FindControl("SelectedPartTypeID"), HiddenField)
            If hf.Value IsNot Nothing AndAlso IsNumeric(hf.Value) Then
                'Recover if server user session was lost while page was still open and user had a selection (align with view state's hidden control)
                ptData = New PartTypeDatasourceControl(hf.Value)
            Else
                ptData = New PartTypeDatasourceControl()
            End If
        End Sub

        ''' <summary>
        ''' Constructor.
        ''' </summary>
        ''' <param name="ThisPage">Pass in "Me"</param>
        ''' <param name="SessionControlName">The variable name in the user's session state. 
        ''' Must be globally unique for this instance.</param>
        ''' <remarks>Instantiate the psuedo-control!</remarks>
        Public Sub New(ByRef ThisPage As System.Web.UI.Page, ByRef SessionControlName As String)
            TheSessionStoreLocation = SessionControlName
            Init(ThisPage)

            'Save State
            HttpContext.Current.Session(TheSessionStoreLocation) = Me
        End Sub

    End Class

End Namespace
