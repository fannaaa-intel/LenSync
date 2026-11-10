Option Strict On
Option Explicit On
Option Infer On

Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

Namespace Modules.Settings

    ''' <summary>
    ''' Settings > Email: the Gmail account that sends password-reset codes. The App Password is stored encrypted and
    ''' never shown again; leaving it blank keeps the saved one.
    ''' </summary>
    Public Class EmailPage
        Inherits ModulePage

        Private ReadOnly _labels As New Dictionary(Of Control, Label)
        Private ReadOnly _txtUser As New LsTextBox()
        Private ReadOnly _txtPassword As New LsTextBox()
        Private ReadOnly _txtFrom As New LsTextBox()
        Private ReadOnly _txtHost As New LsTextBox()
        Private ReadOnly _txtPort As New LsTextBox()
        Private ReadOnly _chkSsl As New LsCheck("Use a secure connection (TLS)")
        Private ReadOnly _notice As New InlineNotice()
        Private ReadOnly _btnSave As New LsButton("Save changes", ButtonVariant.Primary)
        Private ReadOnly _btnTest As New LsButton("Send test email", ButtonVariant.Secondary)
        Private _saved As New EmailSettings()
        Private _filling As Boolean

        Public Sub New()
            MyBase.New("Email", "The Gmail account LENSYNC uses to send password reset codes.")
            AddAction(_btnSave)
            AddAction(_btnTest)
            AddField("Gmail address", _txtUser)
            AddField("App Password", _txtPassword)
            AddField("Sender name", _txtFrom)
            AddField("Mail server (SMTP)", _txtHost)
            AddField("Port", _txtPort)
            _txtUser.Placeholder = "studio@gmail.com"
            _txtUser.MaxLength = 150
            _txtPassword.IsPassword = True
            _txtPassword.MaxLength = 64
            _txtFrom.MaxLength = 80
            _txtFrom.Placeholder = "e.g. LENSYNC Photo Studio"
            _txtHost.MaxLength = 120
            _txtPort.Filter = InputFilter.Digits
            _txtPort.MaxLength = 5
            _chkSsl.Width = _chkSsl.PreferredWidth()
            Controls.AddRange({_chkSsl, _notice})
            _notice.ShowMessage("Use a Gmail App Password, not your normal Gmail password. In your Google Account open " &
                                "Security, turn on 2-Step Verification, then create an App Password for LENSYNC.", NoticeKind.Info, False)

            For Each t In {_txtUser, _txtPassword, _txtFrom, _txtHost, _txtPort}
                AddHandler t.TextChanged, Sub() UpdateButtons()
            Next
            AddHandler _chkSsl.CheckedChanged, Sub() UpdateButtons()
            AddHandler _btnSave.Click, Sub() SaveClicked()
            AddHandler _btnTest.Click, Sub() TestClicked()
            UpdateButtons()
        End Sub

        Private Sub AddField(caption As String, box As LsTextBox)
            _labels(box) = AddFieldLabel(caption)
            box.FieldName = caption
            Controls.Add(box)
        End Sub

        Public Overrides Async Function ReloadAsync() As Task
            Dim s = Await RunAsync(Function() StudioAdmin.LoadEmail(), "Loading email settings")
            If s Is Nothing Then Return
            _saved = s
            Fill()
        End Function

        Private Sub Fill()
            _filling = True
            _txtUser.Text = _saved.User
            _txtPassword.Text = ""
            _txtPassword.Placeholder = If(_saved.HasPassword, "Saved. Leave blank to keep it", "16-letter App Password")
            _txtFrom.Text = _saved.FromName
            _txtHost.Text = _saved.Host
            _txtPort.Text = _saved.Port.ToString()
            _chkSsl.Checked = _saved.UseSsl
            _filling = False
            Validator.ClearAll(Me)
            UpdateButtons()
        End Sub

        Public Overrides ReadOnly Property HasUnsavedChanges As Boolean
            Get
                Return _txtUser.Text <> _saved.User OrElse _txtPassword.Text.Length > 0 OrElse _txtFrom.Text <> _saved.FromName OrElse
                       _txtHost.Text <> _saved.Host OrElse _txtPort.Text <> _saved.Port.ToString() OrElse _chkSsl.Checked <> _saved.UseSsl
            End Get
        End Property

        Public Overrides Sub DiscardChanges()
            Fill()
        End Sub

        Private Sub UpdateButtons()
            If _filling Then Return
            _btnSave.Enabled = HasUnsavedChanges
            _btnTest.Enabled = _saved.HasPassword OrElse _txtPassword.Text.Length > 0
        End Sub

        Private Async Sub SaveClicked()
            If Await SaveAsync() Then Toast.Show("Email settings saved.", ToastKind.Success)
        End Sub

        ''' <summary>Validates and saves; returns True when everything is saved.</summary>
        Private Async Function SaveAsync() As Task(Of Boolean)
            Validator.ClearAll(Me)
            Dim host = _txtHost.Text
            Dim port = _txtPort.Text
            Dim ssl = _chkSsl.Checked
            Dim user = _txtUser.Text
            Dim senderName = _txtFrom.Text
            Dim pw = _txtPassword.Text
            Dim result = Await RunAsync(Function() StudioAdmin.SaveEmail(host, port, ssl, user, senderName, pw), "Saving email settings")
            If result Is Nothing Then Return False
            Dim fields As New Dictionary(Of String, Control) From {
                {"host", _txtHost}, {"port", _txtPort}, {"user", _txtUser}, {"fromName", _txtFrom}, {"password", _txtPassword}}
            If FormErrors.Show(result, fields) Then Return False
            Await ReloadAsync()
            Return True
        End Function

        Private Async Sub TestClicked()
            If HasUnsavedChanges AndAlso Not Await SaveAsync() Then Return
            _btnTest.Enabled = False
            _btnTest.Text = "Sending..."
            Dim result = Await RunAsync(Function() StudioAdmin.SendTestEmail(), "Sending test email")
            _btnTest.Text = "Send test email"
            UpdateButtons()
            If result Is Nothing Then Return
            If result.Ok Then
                LsDialog.Success($"A test email was sent to {CStr(result.Value)}. Check that inbox (and the Spam folder).", "Email works")
            Else
                LsDialog.Error(result.Errors(0).Value, "Test email failed")
            End If
        End Sub

        Protected Overrides Sub LayoutContent(area As Rectangle)
            Dim labelH = Theme.LabelBold.Height + Theme.Dpi(4)
            Dim fieldH = Theme.Dpi(42)
            Dim block = labelH + Theme.Dpi(4) + fieldH + Theme.Dpi(30)
            Dim colGap = Theme.Dpi(48)
            Dim colW = Math.Min(Theme.Dpi(400), (area.Width - colGap) \ 2)
            Dim place = Sub(box As Control, x As Integer, y As Integer, w As Integer)
                            _labels(box).Bounds = New Rectangle(x, y, w, labelH)
                            box.Bounds = New Rectangle(x, y + labelH + Theme.Dpi(4), w, fieldH)
                        End Sub
            Dim leftX = area.Left
            Dim rightX = area.Left + colW + colGap
            place(_txtUser, leftX, area.Top, colW)
            place(_txtPassword, leftX, area.Top + block, colW)
            place(_txtFrom, leftX, area.Top + block * 2, colW)
            place(_txtHost, rightX, area.Top, colW)
            Dim portW = Theme.Dpi(120)
            place(_txtPort, rightX, area.Top + block, portW)
            _chkSsl.Location = New Point(rightX + portW + Theme.Dpi(20), _txtPort.Top + (fieldH - _chkSsl.Height) \ 2)
            Dim noticeTop = area.Top + block * 3 + Theme.Dpi(4)
            _notice.Bounds = New Rectangle(area.Left, noticeTop, Math.Min(area.Width, colW * 2 + colGap), Theme.Dpi(58))
        End Sub

    End Class

End Namespace
