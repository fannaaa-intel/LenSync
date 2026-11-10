Option Strict On
Option Explicit On
Option Infer On

Imports System.Diagnostics
Imports System.IO
Imports System.Text.RegularExpressions
Imports LenSync.Core
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

''' <summary>
''' TEMPORARY Phase 1 test bench: triggers every dialog type, toasts and inline validation.
''' Remove in Phase 2 when LoginForm becomes the startup form.
''' </summary>
Public Class DemoForm
    Inherits Form

    Private ReadOnly _title As New Label()
    Private ReadOnly _subtitle As New Label()
    Private ReadOnly _dialogsCard As New DemoCard("Dialogs", "Enter = focused button  ·  Esc = close / No.")
    Private ReadOnly _toastsCard As New DemoCard("Toasts", "Stacks up to 4. Hover to pause, click to dismiss.")
    Private ReadOnly _validationCard As New DemoCard("Inline validation", "Validate with empty fields, then type to clear.")
    Private ReadOnly _systemCard As New DemoCard("System", "Database, error log and display scale.")

    Private ReadOnly _txtName As New TextBox()
    Private ReadOnly _txtContact As New TextBox()
    Private ReadOnly _cboPackage As New ComboBox()
    Private ReadOnly _txtEmail As New TextBox()
    Private ReadOnly _validationBody As New Panel()
    Private ReadOnly _fields As New List(Of Tuple(Of Label, Control))
    Private _formButtons As LsButton() = {}

    Public Sub New()
        AutoScaleMode = AutoScaleMode.None
        Text = "LENSYNC - Phase 1 test bench"
        BackColor = Theme.Snow
        Font = Theme.Body
        StartPosition = FormStartPosition.CenterScreen
        Dim wa = Screen.PrimaryScreen.WorkingArea
        Size = New Size(Math.Min(Theme.Dpi(1100), wa.Width), Math.Min(Theme.Dpi(720), wa.Height))
        MinimumSize = New Size(Math.Min(Theme.Dpi(980), wa.Width), Math.Min(Theme.Dpi(680), wa.Height))
        WindowState = FormWindowState.Maximized
        DoubleBuffered = True
        KeyPreview = True
        BuildLayout()
    End Sub

    ' ---------------------------------------------------------------- layout
    Private Sub BuildLayout()
        _title.Text = "Phase 1 test bench"
        _title.Font = Theme.Title
        _title.ForeColor = Theme.Ink
        _title.AutoSize = True
        _subtitle.Text = "Try every dialog, toast and validation state. This screen is temporary and goes away in Phase 2."
        _subtitle.Font = Theme.Body
        _subtitle.ForeColor = Theme.Slate
        _subtitle.AutoSize = True
        Controls.AddRange({_title, _subtitle, _dialogsCard, _toastsCard, _validationCard, _systemCard})

        ' Dialogs
        AddButton(_dialogsCard, "Success", ButtonVariant.Primary,
            Sub() LsDialog.Success("Payment saved. Receipt AR-2026-000001 is ready to print.", "Payment recorded"))
        AddButton(_dialogsCard, "Error", ButtonVariant.Primary,
            Sub() LsDialog.Error("The receipt could not be saved because the database did not respond. Nothing was charged.", "Save failed"))
        AddButton(_dialogsCard, "Warning", ButtonVariant.Primary,
            Sub() LsDialog.Warning("This booking is scheduled in the past. Please double-check the date before saving.", "Check the schedule"))
        AddButton(_dialogsCard, "Info", ButtonVariant.Primary,
            Sub() LsDialog.Info("Packages and add-ons are maintained by the Admin under Settings.", "About pricing"))
        AddButton(_dialogsCard, "Confirm", ButtonVariant.Secondary, AddressOf DemoConfirm)
        AddButton(_dialogsCard, "Long message", ButtonVariant.Secondary,
            Sub() LsDialog.Info(String.Join(" ", Enumerable.Repeat(
                "Long messages wrap inside the card and are capped with an ellipsis instead of showing a scrollbar.", 12)),
                "A very long message to check wrapping"))

        ' Toasts
        AddButton(_toastsCard, "Success", ButtonVariant.Primary, Sub() Toast.Show("Booking BK-2026-0009 saved.", ToastKind.Success))
        AddButton(_toastsCard, "Error", ButtonVariant.Primary,
            Sub() Toast.Show("Printer not found. The receipt was saved; you can reprint it later.", ToastKind.Error))
        AddButton(_toastsCard, "Warning", ButtonVariant.Primary,
            Sub() Toast.Show("You are offline from email. Reset codes cannot be sent right now.", ToastKind.Warning))
        AddButton(_toastsCard, "Info", ButtonVariant.Primary, Sub() Toast.Show("Dashboard refreshed.", ToastKind.Info))
        AddButton(_toastsCard, "Burst of 5", ButtonVariant.Secondary, AddressOf DemoBurst)

        ' Validation
        BuildValidationBody()

        ' System
        AddButton(_systemCard, "Test database connection", ButtonVariant.Primary, AddressOf DemoTestDb)
        AddButton(_systemCard, "Simulate a crash", ButtonVariant.Secondary,
            Sub() Throw New InvalidOperationException("Simulated crash from the Phase 1 test bench."))
        AddButton(_systemCard, "Open log folder", ButtonVariant.Ghost, AddressOf DemoOpenLogs)
        Dim scaleInfo As New Label() With {
            .Text = $"Display scale: {CInt(Theme.DpiScale * 100)}%    ·    Logs: {AppLog.Folder}",
            .Font = Theme.Label, .ForeColor = Theme.Slate, .AutoSize = True,
            .Margin = New Padding(0, Theme.Dpi(8), 0, 0)}
        _systemCard.Flow.SetFlowBreak(_systemCard.Flow.Controls(_systemCard.Flow.Controls.Count - 1), True)
        _systemCard.Flow.Controls.Add(scaleInfo)
    End Sub

    Private Sub BuildValidationBody()
        _validationBody.BackColor = Theme.White
        _validationCard.Flow.Visible = False
        _validationCard.Content.Controls.Add(_validationBody)
        _validationBody.Dock = DockStyle.Fill

        AddField("Customer name *", _txtName)
        AddField("Contact no. *  (11 digits)", _txtContact)
        _cboPackage.DropDownStyle = ComboBoxStyle.DropDownList
        _cboPackage.FlatStyle = FlatStyle.Flat
        _cboPackage.Items.AddRange({"Select a package", "Basic Portrait", "Family Portrait", "Graduation Toga", "Birthday Coverage"})
        _cboPackage.SelectedIndex = 0
        AddField("Package *", _cboPackage)
        AddField("Email (optional)", _txtEmail)
        _txtContact.MaxLength = 11

        Dim validate As New LsButton("Validate", ButtonVariant.Primary)
        Dim clear As New LsButton("Clear errors", ButtonVariant.Secondary)
        Dim sample As New LsButton("Fill valid sample", ButtonVariant.Ghost)
        _formButtons = {validate, clear, sample}
        AddHandler _validationBody.Layout,
            Sub(s, e)
                ' A child moving itself (the Validator's shake) must not snap it back mid-animation.
                If e.AffectedControl IsNot Nothing AndAlso e.AffectedControl IsNot _validationBody AndAlso
                   e.AffectedProperty = "Bounds" Then Return
                LayoutValidationBody()
            End Sub
        AddHandler validate.Click, Sub() DemoValidate()
        AddHandler clear.Click, Sub() Validator.ClearAll(_validationBody)
        AddHandler sample.Click,
            Sub()
                _txtName.Text = "Maria Santos"
                _txtContact.Text = "09171234567"
                _cboPackage.SelectedIndex = 1
                _txtEmail.Text = "maria@example.com"
            End Sub
        _validationBody.Controls.AddRange({validate, clear, sample})
    End Sub

    Private Sub AddField(caption As String, field As Control)
        Dim lbl As New Label() With {.Text = caption, .Font = Theme.Label, .ForeColor = Theme.Slate, .AutoEllipsis = True}
        field.Font = Theme.Body
        Dim tb = TryCast(field, TextBox)
        If tb IsNot Nothing Then tb.BorderStyle = BorderStyle.FixedSingle
        _fields.Add(Tuple.Create(lbl, field))
        _validationBody.Controls.Add(lbl)
        _validationBody.Controls.Add(field)
    End Sub

    ''' <summary>Two columns that share the card width; each row leaves room under the field for its error line.</summary>
    Private Sub LayoutValidationBody()
        Dim gap = Theme.Dpi(24)
        Dim colW = Math.Max(Theme.Dpi(120), (_validationBody.ClientSize.Width - gap) \ 2)
        Dim rowH = Theme.Dpi(76)
        For i = 0 To _fields.Count - 1
            Dim x = (i Mod 2) * (colW + gap)
            Dim y = (i \ 2) * rowH
            Dim lbl = _fields(i).Item1
            Dim field = _fields(i).Item2
            lbl.Bounds = New Rectangle(x, y, colW, Theme.Label.Height + Theme.Dpi(2))
            field.Bounds = New Rectangle(x, y + Theme.Dpi(19), colW, field.Height)
        Next
        Dim bx = 0
        Dim by = rowH * ((_fields.Count + 1) \ 2)
        For Each b In _formButtons
            b.Location = New Point(bx, by)
            bx = b.Right + Theme.Dpi(10)
        Next
    End Sub

    Private Shared Sub AddButton(card As DemoCard, text As String, kind As ButtonVariant, onClick As Action)
        Dim b As New LsButton(text, kind)
        b.Margin = New Padding(0, 0, Theme.Dpi(10), Theme.Dpi(10))
        AddHandler b.Click, Sub() onClick()
        card.Flow.Controls.Add(b)
    End Sub

    Protected Overrides Sub OnLayout(e As LayoutEventArgs)
        MyBase.OnLayout(e)
        Dim margin = Theme.Dpi(36)
        Dim gap = Theme.Dpi(16)
        _title.Location = New Point(margin, Theme.Dpi(20))
        _subtitle.Location = New Point(margin + Theme.Dpi(2), _title.Bottom + Theme.Dpi(2))

        Dim top = _subtitle.Bottom + Theme.Dpi(16)
        Dim colW = (ClientSize.Width - margin * 2 - gap) \ 2
        Dim row1H = Theme.Dpi(188)
        Dim row2H = Math.Max(Theme.Dpi(282), ClientSize.Height - top - row1H - gap - Theme.Dpi(24))
        _dialogsCard.Bounds = New Rectangle(margin, top, colW, row1H)
        _toastsCard.Bounds = New Rectangle(margin + colW + gap, top, colW, row1H)
        _validationCard.Bounds = New Rectangle(margin, top + row1H + gap, colW, row2H)
        _systemCard.Bounds = New Rectangle(margin + colW + gap, top + row1H + gap, colW, row2H)
    End Sub

    ' ---------------------------------------------------------------- demo actions
    Private Sub DemoConfirm()
        Dim voidIt = LsDialog.Confirm("Void receipt AR-2026-000001? The booking returns to Unpaid and this cannot be undone.",
                                      "Void this sale?", "Void sale", "Keep it")
        Toast.Show(If(voidIt, "You chose: Void sale", "You chose: Keep it (the safe default)"),
                   If(voidIt, ToastKind.Warning, ToastKind.Info))
    End Sub

    Private Sub DemoBurst()
        Dim kinds = {ToastKind.Info, ToastKind.Success, ToastKind.Warning, ToastKind.Error, ToastKind.Success}
        For i = 0 To kinds.Length - 1
            Toast.Show($"Toast {i + 1} of {kinds.Length}. The oldest leaves when a fifth arrives.", kinds(i))
        Next
    End Sub

    Private Sub DemoValidate()
        Validator.ClearAll(_validationBody)
        If _txtName.Text.Trim().Length < 2 Then
            Validator.SetError(_txtName, "Enter the customer's name.")
        End If
        Dim contact = _txtContact.Text.Trim()
        If contact.Length = 0 Then
            Validator.SetError(_txtContact, "Enter a contact number.")
        ElseIf Not Regex.IsMatch(contact, "^09\d{9}$") Then
            Validator.SetError(_txtContact, "Use 11 digits starting with 09.")
        End If
        If _cboPackage.SelectedIndex <= 0 Then
            Validator.SetError(_cboPackage, "Choose a package.")
        End If
        Dim email = _txtEmail.Text.Trim()
        If email.Length > 0 AndAlso Not Regex.IsMatch(email, "^[^@\s]+@[^@\s]+\.[^@\s]+$") Then
            Validator.SetError(_txtEmail, "Enter a valid email address.")
        End If

        If Validator.HasErrors(_validationBody) Then
            Validator.FocusFirstError(_validationBody)
        Else
            Toast.Show("All fields look good.", ToastKind.Success)
        End If
    End Sub

    Private Sub DemoTestDb()
        Dim message As String = Nothing
        Cursor = Cursors.WaitCursor
        Dim ok As Boolean
        Dim tables As Integer
        Try
            ok = Db.TestConnection(message)
            If ok Then tables = Db.Scalar(Of Integer)("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE()")
        Catch ex As Exception
            AppLog.Write(ex, "DemoTestDb")
            ok = False
            message = Db.FriendlyMessage(ex)
        Finally
            Cursor = Cursors.Default
        End Try
        If ok Then
            LsDialog.Success($"{message} {tables} tables found.", "Database connected")
        Else
            LsDialog.Error(message, "Database unavailable")
        End If
    End Sub

    Private Sub DemoOpenLogs()
        Directory.CreateDirectory(AppLog.Folder)
        Process.Start("explorer.exe", AppLog.Folder)
    End Sub

End Class

''' <summary>White rounded card used only by the test bench.</summary>
Friend NotInheritable Class DemoCard
    Inherits Panel

    Private ReadOnly _heading As New Label()
    Private ReadOnly _description As New Label()

    Public ReadOnly Property Content As New Panel()
    Public ReadOnly Property Flow As New FlowLayoutPanel()

    Public Sub New(heading As String, description As String)
        DoubleBuffered = True
        SetStyle(ControlStyles.ResizeRedraw, True)
        BackColor = Theme.Snow
        _heading.Text = heading
        _heading.Font = Theme.BodyBold
        _heading.ForeColor = Theme.Ink
        _heading.AutoSize = True
        _heading.BackColor = Theme.White
        _description.Text = description
        _description.Font = Theme.Label
        _description.ForeColor = Theme.Slate
        _description.AutoEllipsis = True
        _description.BackColor = Theme.White
        Content.BackColor = Theme.White
        Flow.BackColor = Theme.White
        Flow.Dock = DockStyle.Fill
        Flow.WrapContents = True
        Flow.AutoScroll = False
        Content.Controls.Add(Flow)
        Controls.AddRange({_heading, _description, Content})
    End Sub

    Protected Overrides Sub OnLayout(e As LayoutEventArgs)
        MyBase.OnLayout(e)
        Dim pad = Theme.Dpi(Theme.Pad)
        _heading.Location = New Point(pad, pad - Theme.Dpi(4))
        _description.Bounds = New Rectangle(pad, _heading.Bottom + Theme.Dpi(2), Math.Max(0, Width - pad * 2), Theme.Label.Height + Theme.Dpi(2))
        Dim top = _description.Bottom + Theme.Dpi(16)
        Content.Bounds = New Rectangle(pad, top, Math.Max(0, Width - pad * 2), Math.Max(0, Height - top - pad + Theme.Dpi(8)))
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Dim g = e.Graphics
        Gfx.HighQuality(g)
        Dim r As New RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F)
        Using path = Gfx.RoundedRect(r, Theme.Dpi(CSng(Theme.Radius)))
            Using b As New SolidBrush(Theme.White)
                g.FillPath(b, path)
            End Using
            Using p As New Pen(Theme.Mist)
                g.DrawPath(p, path)
            End Using
        End Using
    End Sub

End Class
