Option Strict On
Option Explicit On
Option Infer On

Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

''' <summary>
''' Change the signed-in user's password. Forced mode (first sign-in with the temporary password) skips the
''' current-password field and offers "Log out" instead of "Cancel".
''' </summary>
Public Class ChangePasswordDialog
    Inherits LsFormDialog

    Private Const FieldBlock As Integer = 92     ' label + field + room for a validation line (96-dpi)

    Private ReadOnly _forced As Boolean
    Private ReadOnly _txtCurrent As LsTextBox
    Private ReadOnly _txtNew As New LsTextBox()
    Private ReadOnly _txtConfirm As New LsTextBox()
    Private ReadOnly _checklist As New PasswordChecklist()

    Public Sub New(forced As Boolean)
        MyBase.New(If(forced, "First sign-in", "Security"),
                   If(forced, "Set your own password", "Change password"),
                   If(forced, "You logged in with a temporary password. Replace it with one only you know.",
                              "Use a strong password that you don't use anywhere else."),
                   "Save password", If(forced, "Log out", "Cancel"), 360, BodyHeightFor(forced))
        _forced = forced
        StrongFrame = forced

        Dim y = 0
        If Not forced Then
            _txtCurrent = New LsTextBox()
            AddField("Current password", _txtCurrent, y)
            y += Theme.Dpi(FieldBlock)
        End If
        AddField("New password", _txtNew, y)
        y += Theme.Dpi(FieldBlock)
        AddField("Confirm new password", _txtConfirm, y)
        y += Theme.Dpi(FieldBlock) + Theme.Dpi(4)
        _checklist.Bounds = New Rectangle(0, y, Body.Width, _checklist.PreferredHeight)
        Body.Controls.Add(_checklist)
        AddHandler _txtNew.TextChanged, Sub() _checklist.Evaluate(_txtNew.Text)
    End Sub

    Private Shared Function BodyHeightFor(forced As Boolean) As Integer
        Return If(forced, 2, 3) * FieldBlock + 4 + 4 * 22
    End Function

    Private Sub AddField(caption As String, box As LsTextBox, y As Integer)
        Dim lbl As New Label() With {
            .Text = caption, .Font = Theme.LabelBold, .ForeColor = Theme.Charcoal,
            .Bounds = New Rectangle(0, y, Body.Width, Theme.LabelBold.Height + Theme.Dpi(4)),
            .TextAlign = ContentAlignment.BottomLeft}
        box.FieldName = caption
        box.IsPassword = True
        box.Icon = LineIcon.Lock
        box.MaxLength = Passwords.MaxLength
        box.Bounds = New Rectangle(0, lbl.Bottom + Theme.Dpi(4), Body.Width, Theme.Dpi(42))
        Body.Controls.Add(lbl)
        Body.Controls.Add(box)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        If _txtCurrent IsNot Nothing Then _txtCurrent.Focus() Else _txtNew.Focus()
    End Sub

    Protected Overrides Async Function OnSubmitAsync() As Task(Of Boolean)
        Validator.ClearAll(Body)
        Dim current = If(_txtCurrent?.Text, Nothing)
        Dim newPassword = _txtNew.Text
        If _txtCurrent IsNot Nothing AndAlso current.Length = 0 Then Validator.SetError(_txtCurrent, "Enter your current password.")
        If newPassword.Length = 0 Then
            Validator.SetError(_txtNew, "Enter a new password.")
        Else
            Dim problem = Passwords.Problem(newPassword, Session.CurrentUser?.Username)
            If problem IsNot Nothing Then
                Dim allRulesMet = Passwords.Rules.All(Function(r) r.Test(newPassword))
                Validator.SetError(_txtNew, If(allRulesMet, problem, "Meet every rule in the list below."))
            End If
        End If
        If _txtConfirm.Text <> newPassword Then Validator.SetError(_txtConfirm, "The two passwords do not match.")
        If Validator.HasErrors(Body) Then
            Shake()
            Validator.FocusFirstError(Body)
            Return False
        End If

        SetBusy(True, "Saving...")
        Dim problemFromServer = Await Task.Run(Function() Auth.ChangePassword(If(_forced, Nothing, current), newPassword))
        SetBusy(False)
        If problemFromServer IsNot Nothing Then
            Dim target As Control = _txtNew
            If _txtCurrent IsNot Nothing AndAlso problemFromServer.IndexOf("current password", StringComparison.OrdinalIgnoreCase) >= 0 Then
                target = _txtCurrent
            End If
            Validator.SetError(target, problemFromServer)
            Shake()
            target.Focus()
            Return False
        End If
        Toast.Show("Your password was changed.", ToastKind.Success)
        Return True
    End Function

    Protected Overrides Sub OnSecondary()
        If IsBusy Then Return
        CloseCard(DialogResult.Cancel)
    End Sub

End Class
