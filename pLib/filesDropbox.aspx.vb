﻿Imports apiDropbox
Imports System.Data


Partial Class pAdmin_filesDropbox
    Inherits System.Web.UI.Page
    Protected theDropbox As apiDropbox.DropboxUser

    Protected Sub Page_Init(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Init
        If Not HttpContext.Current.Session("dropbox.Cache.Contents") Is Nothing Then
            'Restore datasource from cache -- so we don't keep banging on Dropbox's servers
            xGridDropboxContents.DataBind()
            xGridDropboxContents.DataSource = HttpContext.Current.Session("dropbox.Cache.Contents")
        End If
        'Rescan server disk if last scan was more than 5 minutes ago...
        If DropboxServer.HowStale.TotalSeconds > 300 Then
            DropboxServer.Update()
        End If
        xGridServer.DataSource = apiDropbox.DropboxServer.GetDataSource
        xGridServer.DataBind()
    End Sub

    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load
        suLoginRequired(Me) 'Access Control

        'Login
        If HttpContext.Current.Session("dropbox.Account") Is Nothing Then
            'Save
            theDropbox = New apiDropbox.dropboxUser(HttpContext.Current.Session("user.UserID"))
            HttpContext.Current.Session("dropbox.Account") = theDropbox 'Save!
        Else
            'Restore
            theDropbox = HttpContext.Current.Session("dropbox.Account")
        End If

        If Not (IsCallback Or IsPostBack) Then
            'Initial Page Load (Delete prior state if still present)
            If Not HttpContext.Current.Session("dropbox.Cache.Contents") Is Nothing Then
                HttpContext.Current.Session("dropbox.Cache.Contents") = Nothing
            End If

            'Default Tab
            xTabPages.ActiveTabIndex = 0

            'Update Status
            lblAccountHolder.Text = theDropbox.GetAccountInfo.display_name & " (" & theDropbox.GetAccountInfo.email & ")"
            If Not HttpContext.Current.Session("dropbox.Status") Is Nothing Then
                lblStatus.Text = HttpContext.Current.Session("dropbox.Status")
            Else
                lblStatus.Text = "Uninitialized (Null)"
            End If
        End If
    End Sub

    Protected Sub xGridSelectUser_FocusedRowChanged(ByVal sender As Object, ByVal e As System.EventArgs) Handles xGridSelectUser.FocusedRowChanged
        'Selected new user!
        Dim dr As DataRow = xGridSelectUser.GetDataRow(xGridSelectUser.FocusedRowIndex)
        If Not dr Is Nothing Then
            Dim dUser As New DropboxUser(dr.Field(Of Integer)("UserID"))
            HttpContext.Current.Session("dropbox.Cache.Contents") = _
                dUser.GetContents
            xGridDropboxContents.DataSource = HttpContext.Current.Session("dropbox.Cache.Contents")
            xGridDropboxContents.DataBind()
        End If
    End Sub

    ''' <summary>
    ''' Deletes all files in a user's FriedParts Dropbox Folder. This effectively resets the
    ''' folder. It does not effect any other user as the libraries are still registered with
    ''' FriedParts. The correct files will simply be replaced on the next sync pass performed
    ''' by a background worker. This is useful in case your folder gets full of extra junk files.
    ''' </summary>
    ''' <param name="sender"></param>
    ''' <param name="e"></param>
    ''' <remarks>Event Handler</remarks>
    Protected Sub btnDeleteAllFiles_Click(ByVal sender As Object, ByVal e As System.EventArgs) Handles btnDeleteAllFiles.Click
        'Get selected user!
        Dim dr As DataRow = xGridSelectUser.GetDataRow(xGridSelectUser.FocusedRowIndex)
        If Not dr Is Nothing Then
            Dim dUser As New DropboxUser(dr.Field(Of Integer)("UserID"))
            dUser.deleteAllFiles()
            'Update display
            HttpContext.Current.Session("dropbox.Cache.Contents") = _
               dUser.GetContents
            xGridDropboxContents.DataSource = HttpContext.Current.Session("dropbox.Cache.Contents")
            xGridDropboxContents.DataBind()
        End If
    End Sub

    Protected Sub xGridServer_HtmlRowPrepared(ByVal sender As Object, ByVal e As DevExpress.Web.ASPxGridView.ASPxGridViewTableRowEventArgs) Handles xGridServer.HtmlRowPrepared
        'Fires for each row of the grid after data is loaded into it, but before formatting. 
        '   Used to implement data specific display issues (like hilighting files you own)
        If suGetUsername().CompareTo(Convert.ToString(e.GetValue("Owner"))) = 0 Then
            e.Row.Font.Bold = True
        End If
    End Sub
End Class
