Option Strict On
Option Explicit On
Option Infer On

Imports System.Threading.Tasks
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

''' <summary>
''' Shows a temporary password ONCE (after creating a user or an admin reset). It is never stored in plain text,
''' so the Admin copies it or reads it out now. Enter/Esc = Done.
''' </summary>
Public Class TempPasswordDialog
    Inherits LsFormDialog

    Private ReadOnly _password As String
    Private ReadOnly _username As String

    Public Sub New(fullName As String, username As String, password As String, isNewAccount As Boolean)
        MyBase.New("Temporary password",
                   If(isNewAccount, $"{fullName} can now log in", $"New password for {fullName}"),
                   "Give them this password now. They must replace it the first time they log in, and it won't be shown again.",
                   "Done", "Copy password", 360, 112)
        _password = password
        _username = username
        StrongFrame = True
        AddHandler Body.Paint, AddressOf PaintPassword
    End Sub

    Private Sub PaintPassword(sender As Object, e As PaintEventArgs)
        Dim g = e.Graphics
        Gfx.HighQuality(g)
        Dim r As New RectangleF(0.5F, 0.5F, Body.Width - 1.0F, Body.Height - 1.0F)
        Using path = Gfx.RoundedRect(r, Theme.Dpi(CSng(Theme.Radius)))
            Using b As New SolidBrush(Theme.Snow)
                g.FillPath(b, path)
            End Using
        End Using
        Dim pad = Theme.Dpi(20)
        TextRenderer.DrawText(g, "Username  " & _username, Theme.Label, New Rectangle(pad, Theme.Dpi(16), Body.Width - pad * 2, Theme.Label.Height + 2),
                              Theme.Slate, TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis)
        Gfx.DrawTracked(g, _password, Theme.Title, Theme.Ink, Body.Width / 2.0F, Theme.Dpi(16) + Theme.Label.Height + Theme.Dpi(14),
                        Theme.Dpi(3.0F), StringAlignment.Center)
    End Sub

    Protected Overrides Function OnSubmitAsync() As Task(Of Boolean)
        Return Task.FromResult(True)
    End Function

    ''' <summary>The secondary button copies instead of closing.</summary>
    Protected Overrides Sub OnSecondary()
        Try
            Clipboard.SetText(_password)
            SetSecondaryText("Copied")
            Toast.Show("Password copied. Paste it only where the user will see it.", ToastKind.Info)
        Catch ex As Runtime.InteropServices.ExternalException
            Toast.Show("The clipboard is busy. Read the password out instead.", ToastKind.Warning)
        End Try
    End Sub

    Protected Overrides Sub OnEscapePressed()
        CloseCard(DialogResult.OK)
    End Sub

End Class
