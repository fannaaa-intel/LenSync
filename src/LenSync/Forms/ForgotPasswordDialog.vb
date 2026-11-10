Option Strict On
Option Explicit On
Option Infer On

Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

''' <summary>
''' Forgot password in three steps on one card: (1) username + registered email -> code is emailed,
''' (2) enter the 6-digit code, (3) set a new password. Each step slides in; the card resizes smoothly.
''' Returns OK when the password was changed.
''' </summary>
Public Class ForgotPasswordDialog
    Inherits LsFormDialog

    Private Const BodyWidth As Integer = 360
    Private Const FieldBlock As Integer = 92
    Private Const NoticeHeight As Integer = 52

    Private ReadOnly _steps(2) As Panel
    Private _step As Integer = 0
    Private ReadOnly _slideTween As New Tween()

    Private ReadOnly _txtUser As New LsTextBox()
    Private ReadOnly _txtEmail As New LsTextBox()
    Private ReadOnly _lblCodeInfo As New Label()
    Private ReadOnly _txtCode As New LsTextBox()
    Private ReadOnly _btnResend As New LsButton("Send a new code", ButtonVariant.Ghost)
    Private ReadOnly _txtNew As New LsTextBox()
    Private ReadOnly _txtConfirm As New LsTextBox()
    Private ReadOnly _checklist As New PasswordChecklist()
    Private ReadOnly _notice As New InlineNotice()

    Private _userId As Integer
    Private _resetId As Integer
    Private _username As String = ""
    Private _email As String = ""

    Public Sub New(Optional username As String = "")
        MyBase.New("Step 1 of 3", "Reset your password",
                   "Enter your username and the email registered to your account. We'll email you a 6-digit code.",
                   "Send code", "Back to log in", BodyWidth, StepHeight(0))
        For i = 0 To 2
            _steps(i) = New Panel() With {.BackColor = Theme.White, .Visible = (i = 0)}
            Body.Controls.Add(_steps(i))
        Next
        Body.Controls.Add(_notice)
        AddHandler Body.Resize, Sub() LayoutBody()

        ' Step 1
        AddField(_steps(0), "Username", _txtUser, 0, LineIcon.User, False)
        AddField(_steps(0), "Registered email", _txtEmail, FieldBlock, Nothing, False)
        _txtUser.Text = username
        _txtUser.MaxLength = 50
        _txtEmail.MaxLength = 150
        _txtEmail.Placeholder = "name@example.com"

        ' Step 2
        _lblCodeInfo.Font = Theme.Body
        _lblCodeInfo.ForeColor = Theme.Charcoal
        _lblCodeInfo.Bounds = New Rectangle(0, 0, Theme.Dpi(BodyWidth), Theme.Body.Height * 2 + Theme.Dpi(6))
        _steps(1).Controls.Add(_lblCodeInfo)
        AddField(_steps(1), "6-digit code", _txtCode, 56, LineIcon.Key, False)
        _txtCode.MaxLength = 6
        _txtCode.Placeholder = "000000"
        AddHandler _txtCode.InnerTextBox.KeyPress, AddressOf DigitsOnly
        AddHandler _txtCode.TextChanged, AddressOf OnCodeChanged
        _btnResend.FitToText()
        _btnResend.Location = New Point(-Theme.Dpi(12), Theme.Dpi(56 + FieldBlock - 8))
        AddHandler _btnResend.Click, AddressOf OnResendClick
        _steps(1).Controls.Add(_btnResend)

        ' Step 3
        AddField(_steps(2), "New password", _txtNew, 0, LineIcon.Lock, True)
        AddField(_steps(2), "Confirm new password", _txtConfirm, FieldBlock, LineIcon.Lock, True)
        _checklist.Bounds = New Rectangle(0, Theme.Dpi(FieldBlock * 2 + 4), Theme.Dpi(BodyWidth), _checklist.PreferredHeight)
        _steps(2).Controls.Add(_checklist)
        AddHandler _txtNew.TextChanged, Sub() _checklist.Evaluate(_txtNew.Text)

        LayoutBody()
    End Sub

    Private Shared Function StepHeight(stepIndex As Integer) As Integer
        Select Case stepIndex
            Case 0 : Return FieldBlock * 2 + NoticeHeight
            Case 1 : Return 56 + FieldBlock + 34 + NoticeHeight
            Case Else : Return FieldBlock * 2 + 4 + 4 * 22 + 8 + NoticeHeight
        End Select
    End Function

    Private Sub AddField(host As Panel, caption As String, box As LsTextBox, y96 As Integer, icon As LineIcon?, password As Boolean)
        Dim y = Theme.Dpi(y96)
        Dim lbl As New Label() With {
            .Text = caption, .Font = Theme.LabelBold, .ForeColor = Theme.Charcoal,
            .Bounds = New Rectangle(0, y, Theme.Dpi(BodyWidth), Theme.LabelBold.Height + Theme.Dpi(4)),
            .TextAlign = ContentAlignment.BottomLeft}
        box.FieldName = caption
        box.Icon = icon
        box.IsPassword = password
        If password Then box.MaxLength = Passwords.MaxLength
        box.Bounds = New Rectangle(0, lbl.Bottom + Theme.Dpi(4), Theme.Dpi(BodyWidth), Theme.Dpi(42))
        host.Controls.Add(lbl)
        host.Controls.Add(box)
    End Sub

    Private Sub LayoutBody()
        Dim noticeH = Theme.Dpi(NoticeHeight - 6)
        _notice.Bounds = New Rectangle(0, Body.Height - noticeH, Body.Width, noticeH)
        For Each p In _steps
            If Not _slideTween.IsRunning Then p.Left = 0
            p.Top = 0
            p.Size = New Size(Body.Width, Math.Max(0, Body.Height - noticeH - Theme.Dpi(6)))
        Next
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        If _txtUser.Text.Length > 0 Then _txtEmail.Focus() Else _txtUser.Focus()
    End Sub

    ' ---------------------------------------------------------------- steps
    Private Sub GoToStep(index As Integer)
        Dim previous = _steps(_step)
        _step = index
        _notice.HideMessage()
        Select Case index
            Case 0
                SetHeader("Step 1 of 3", "Reset your password",
                          "Enter your username and the email registered to your account. We'll email you a 6-digit code.")
                SetPrimaryText("Send code")
                SetSecondaryText("Back to log in")
            Case 1
                SetHeader("Step 2 of 3", "Check your email", Nothing)
                _lblCodeInfo.Text = $"We sent a 6-digit code to {Mailer.MaskEmail(_email)}. It expires in {Passwords.ResetCodeMinutes} minutes."
                SetPrimaryText("Verify code")
                SetSecondaryText("Back")
                _txtCode.Text = ""
            Case 2
                SetHeader("Step 3 of 3", "Choose a new password", "Pick something strong that only you know.")
                SetPrimaryText("Save password")
                SetSecondaryText("Cancel")
        End Select
        SetBodyHeight(StepHeight(index))

        ' Slide the new step in from the right.
        Dim incoming = _steps(index)
        previous.Visible = False
        incoming.Visible = True
        Dim offset = Theme.Dpi(36)
        _slideTween.Start(Theme.OpenMs,
            Sub(t) incoming.Left = CInt(offset * (1 - Easing.OutCubic(t))),
            Sub() incoming.Left = 0)
        Select Case index
            Case 0 : _txtEmail.Focus()
            Case 1 : _txtCode.Focus()
            Case 2 : _txtNew.Focus()
        End Select
    End Sub

    Protected Overrides Sub OnSecondary()
        If IsBusy Then Return
        If _step = 1 Then
            GoToStep(0)
        Else
            CloseCard(DialogResult.Cancel)
        End If
    End Sub

    Protected Overrides Async Function OnSubmitAsync() As Task(Of Boolean)
        Select Case _step
            Case 0 : Await RequestCodeAsync(fromStep:=0)
            Case 1 : Await VerifyCodeAsync()
            Case 2 : Return Await SavePasswordAsync()
        End Select
        Return False
    End Function

    ' ---------------------------------------------------------------- step 1: request the code
    Private Async Function RequestCodeAsync(fromStep As Integer) As Task
        If fromStep = 0 Then
            Validator.ClearAll(_steps(0))
            Dim u = _txtUser.Text.Trim()
            Dim em = _txtEmail.Text.Trim()
            If u.Length = 0 Then Validator.SetError(_txtUser, "Enter your username.")
            If em.Length = 0 Then
                Validator.SetError(_txtEmail, "Enter your registered email.")
            ElseIf Not Regex.IsMatch(em, "^[^@\s]+@[^@\s]+\.[^@\s]+$") Then
                Validator.SetError(_txtEmail, "Enter a valid email address.")
            End If
            If Validator.HasErrors(_steps(0)) Then
                Shake()
                Validator.FocusFirstError(_steps(0))
                Return
            End If
            _username = u
            _email = em
        End If

        SetBusy(True, "Sending...")
        _btnResend.Enabled = False
        Dim user = _username
        Dim mail = _email
        Dim result = Await Task.Run(Function() Auth.RequestResetCode(user, mail))
        SetBusy(False)
        _btnResend.Enabled = True
        If Not result.Ok Then
            _notice.ShowMessage(result.Message, NoticeKind.Error)
            Shake()
            Return
        End If
        _userId = result.UserId
        If fromStep = 0 Then
            GoToStep(1)
        Else
            _notice.ShowMessage("A new code is on its way. Earlier codes no longer work.", NoticeKind.Success)
            _txtCode.Text = ""
            _txtCode.Focus()
        End If
    End Function

    Private Async Sub OnResendClick(sender As Object, e As EventArgs)
        If IsBusy Then Return
        Await RequestCodeAsync(fromStep:=1)
    End Sub

    ' ---------------------------------------------------------------- step 2: verify the code
    Private Sub DigitsOnly(sender As Object, e As KeyPressEventArgs)
        If Not Char.IsControl(e.KeyChar) AndAlso Not Char.IsDigit(e.KeyChar) Then e.Handled = True
    End Sub

    Private Sub OnCodeChanged(sender As Object, e As EventArgs)
        ' Pasted text may contain spaces or dashes ("123 456"): keep the digits only.
        Dim digits = New String(_txtCode.Text.Where(AddressOf Char.IsDigit).ToArray())
        If digits <> _txtCode.Text Then
            _txtCode.Text = digits
            _txtCode.InnerTextBox.SelectionStart = digits.Length
            Return
        End If
        ' Six digits typed: verify right away.
        If digits.Length = 6 AndAlso _step = 1 AndAlso Not IsBusy Then PrimaryButton.PerformClick()
    End Sub

    Private Async Function VerifyCodeAsync() As Task
        Validator.ClearAll(_steps(1))
        Dim code = _txtCode.Text.Trim()
        If code.Length <> 6 Then
            Validator.SetError(_txtCode, "Enter all 6 digits from the email.")
            Shake()
            _txtCode.Focus()
            Return
        End If
        SetBusy(True, "Checking...")
        Dim uid = _userId
        Dim result = Await Task.Run(Function() Auth.VerifyResetCode(uid, code))
        SetBusy(False)
        If Not result.Ok Then
            _notice.ShowMessage(result.Message, NoticeKind.Error)
            Shake()
            _txtCode.Text = ""
            _txtCode.Focus()
            Return
        End If
        _resetId = result.ResetId
        GoToStep(2)
    End Function

    ' ---------------------------------------------------------------- step 3: new password
    Private Async Function SavePasswordAsync() As Task(Of Boolean)
        Validator.ClearAll(_steps(2))
        Dim pw = _txtNew.Text
        If pw.Length = 0 Then
            Validator.SetError(_txtNew, "Enter a new password.")
        Else
            Dim problem = Passwords.Problem(pw, _username)
            If problem IsNot Nothing Then
                Validator.SetError(_txtNew, If(Passwords.Rules.All(Function(r) r.Test(pw)), problem, "Meet every rule in the list below."))
            End If
        End If
        If _txtConfirm.Text <> pw Then Validator.SetError(_txtConfirm, "The two passwords do not match.")
        If Validator.HasErrors(_steps(2)) Then
            Shake()
            Validator.FocusFirstError(_steps(2))
            Return False
        End If

        SetBusy(True, "Saving...")
        Dim uid = _userId
        Dim rid = _resetId
        Dim problemFromServer = Await Task.Run(Function() Auth.CompleteReset(uid, rid, pw))
        SetBusy(False)
        If problemFromServer IsNot Nothing Then
            _notice.ShowMessage(problemFromServer, NoticeKind.Error)
            Shake()
            Return False
        End If
        Return True
    End Function

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then _slideTween.Dispose()
        MyBase.Dispose(disposing)
    End Sub

End Class
