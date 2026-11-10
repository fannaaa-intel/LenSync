Option Strict On
Option Explicit On
Option Infer On

Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.Models
Imports LenSync.Modules.AddService
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

''' <summary>
''' Create or edit a booking (Add Service > Bookings): customer, package, schedule and the downpayment. Three columns
''' so the whole form fits on short screens at 150% scaling.
''' </summary>
Public Class BookingDialog
    Inherits LsFormDialog

    Private Const ColW As Integer = 280
    Private Const ColGap As Integer = 20
    Private Const RowH As Integer = 86
    Private Const BodyW As Integer = ColW * 3 + ColGap * 2

    Private ReadOnly _existing As Booking
    Private ReadOnly _packages As List(Of PriceItem)
    Private ReadOnly _times As New List(Of TimeSpan)
    Private ReadOnly _txtName As New LsTextBox()
    Private ReadOnly _txtContact As New LsTextBox()
    Private ReadOnly _txtEmail As New LsTextBox()
    Private ReadOnly _package As New LsSelect()
    Private ReadOnly _date As New LsDatePicker()
    Private ReadOnly _time As New LsSelect()
    Private ReadOnly _txtDp As New LsTextBox()
    Private ReadOnly _dpMethod As New LsChoice()
    Private ReadOnly _txtRef As New LsTextBox()
    Private ReadOnly _txtNotes As New LsTextBox()
    Private ReadOnly _chkDone As LsCheck

    Public Property SavedId As Integer
    ''' <summary>True if the user ticked "shoot done" (the caller offers billing right away).</summary>
    Public Property MarkedDone As Boolean

    Public Sub New(packages As List(Of PriceItem), Optional existing As Booking = Nothing)
        MyBase.New(If(existing Is Nothing, "New booking", "Edit booking"),
                   If(existing Is Nothing, "Book a shoot", $"{existing.BookingNo}  ·  {existing.CustomerName}"),
                   Nothing,
                   If(existing Is Nothing, "Save booking", "Save changes"), "Cancel", BodyW, RowH * 4 - 6)
        _existing = existing
        _packages = New List(Of PriceItem)(packages)
        ' An old booking may use a package that was deactivated later; keep it selectable for that booking.
        If existing IsNot Nothing AndAlso existing.PackageId.HasValue AndAlso Not _packages.Any(Function(p) p.Id = existing.PackageId.Value) Then
            _packages.Add(New PriceItem With {.Id = existing.PackageId.Value, .Name = existing.PackageName, .Price = existing.PackagePrice})
        End If

        _chkDone = New LsCheck(If(existing Is Nothing, "Shoot done now (walk-in)", "Shoot is done (ready to bill)"))
        Dim showDone = existing Is Nothing OrElse existing.Status = BookingStatus.Pending

        Place("Customer name", _txtName, 0, 0)
        Place("Contact number", _txtContact, 1, 0)
        Place("Email (optional)", _txtEmail, 2, 0)
        Place("Package", _package, 0, 1)
        Place("Date", _date, 1, 1)
        Place("Time", _time, 2, 1)
        Place("Downpayment (₱)", _txtDp, 0, 2)
        Place("Downpayment paid via", _dpMethod, 1, 2)
        Place("Reference no. (GCash/Maya)", _txtRef, 2, 2)
        Place("Notes (optional)", _txtNotes, 0, 3, span:=2)
        _chkDone.Bounds = New Rectangle(Theme.Dpi((ColW + ColGap) * 2), Theme.Dpi(RowH * 3 + 24) + (Theme.Dpi(40) - _chkDone.Height) \ 2,
                                        Theme.Dpi(ColW), _chkDone.Height)
        _chkDone.Visible = showDone
        Body.Controls.Add(_chkDone)

        _txtName.MaxLength = 120
        _txtName.Placeholder = "e.g. Maria Santos"
        _txtContact.MaxLength = 20
        _txtContact.Filter = InputFilter.Phone
        _txtContact.Placeholder = "e.g. 0917 123 4567"
        _txtEmail.MaxLength = 150
        _txtEmail.Placeholder = "name@gmail.com"
        _package.Placeholder = "Choose a package"
        _package.SetItems(_packages.Select(Function(p) p.Name), _packages.Select(Function(p) Money.Format(p.Price)))
        _time.Icon = LineIcon.Clock
        _time.MaxVisibleItems = 7
        _txtDp.Filter = InputFilter.Money
        _txtDp.MaxLength = 13
        _txtDp.Placeholder = "0.00 if none"
        _dpMethod.SetItems(PaymentMethods.All)
        _txtRef.MaxLength = 40
        _txtNotes.MaxLength = BookingAdmin.MaxNotes
        _txtNotes.Placeholder = "e.g. Family of 6, white backdrop"
        AddHandler _txtDp.InnerTextBox.Leave, AddressOf FormatDownpayment
        AddHandler _dpMethod.SelectedIndexChanged, Sub() UpdateReference()
        AddHandler _chkDone.CheckedChanged, AddressOf OnDoneChanged

        For m = 6 * 60 To 21 * 60 Step 30
            _times.Add(TimeSpan.FromMinutes(m))
        Next

        If existing Is Nothing Then
            _date.MinDate = Date.Today
            _date.Value = Date.Today
            SetTime(New TimeSpan(9, 0, 0))
        Else
            _txtName.Text = existing.CustomerName
            _txtContact.Text = existing.ContactNo
            _txtEmail.Text = existing.CustomerEmail
            _package.SelectedIndex = If(existing.PackageId.HasValue, _packages.FindIndex(Function(p) p.Id = existing.PackageId.Value), -1)
            _date.MinDate = If(existing.ScheduleAt.Date < Date.Today, existing.ScheduleAt.Date, Date.Today)
            _date.Value = existing.ScheduleAt.Date
            SetTime(existing.ScheduleAt.TimeOfDay)
            If existing.Downpayment > 0D Then _txtDp.Text = Money.FormatPlain(existing.Downpayment)
            _dpMethod.SelectedIndex = Array.IndexOf(PaymentMethods.All, existing.DownpaymentMethod)
            _txtRef.Text = existing.DownpaymentRef
            _txtNotes.Text = existing.Notes
        End If
        UpdateReference()
    End Sub

    ''' <summary>Label + field in column <paramref name="col"/>, row <paramref name="row"/> (96-dpi grid).</summary>
    Private Sub Place(caption As String, field As Control, col As Integer, row As Integer, Optional span As Integer = 1)
        Dim x = Theme.Dpi((ColW + ColGap) * col)
        Dim w = Theme.Dpi(ColW * span + ColGap * (span - 1))
        Dim lbl As New Label() With {
            .Text = caption, .Font = Theme.LabelBold, .ForeColor = Theme.Charcoal, .TextAlign = ContentAlignment.BottomLeft,
            .AutoEllipsis = True, .Bounds = New Rectangle(x, Theme.Dpi(RowH * row), w, Theme.LabelBold.Height + Theme.Dpi(4))}
        field.Bounds = New Rectangle(x, lbl.Bottom + Theme.Dpi(4), w, Theme.Dpi(40))
        field.AccessibleName = caption
        Dim box = TryCast(field, LsTextBox)
        If box IsNot Nothing Then box.FieldName = caption
        Body.Controls.Add(lbl)
        Body.Controls.Add(field)
    End Sub

    ''' <summary>Selects <paramref name="t"/>, adding it to the half-hour slots if it's an odd time (e.g. a walk-in).</summary>
    Private Sub SetTime(t As TimeSpan)
        t = New TimeSpan(t.Hours, t.Minutes, 0)
        If Not _times.Contains(t) Then
            _times.Add(t)
            _times.Sort()
        End If
        _time.SetItems(_times.Select(Function(x) Display.Time(x)))
        _time.SelectedIndex = _times.IndexOf(t)
    End Sub

    Private Sub OnDoneChanged(sender As Object, e As EventArgs)
        ' A walk-in is happening now: put it on today's date and the current time.
        If _existing Is Nothing AndAlso _chkDone.Checked Then
            _date.Value = Date.Today
            Dim current = DateTime.Now.TimeOfDay
            SetTime(New TimeSpan(current.Hours, current.Minutes, 0))
        End If
    End Sub

    Private Sub UpdateReference()
        Dim ewallet = _dpMethod.SelectedIndex >= 0 AndAlso PaymentMethods.IsEWallet(PaymentMethods.All(_dpMethod.SelectedIndex))
        _txtRef.Enabled = ewallet
        _txtRef.Placeholder = If(ewallet, "From the e-wallet receipt", "For GCash or Maya only")
        If Not ewallet Then Validator.Clear(_txtRef)
    End Sub

    Private Sub FormatDownpayment(sender As Object, e As EventArgs)
        Dim amount As Decimal
        Dim problem As String = Nothing
        If _txtDp.Text.Trim().Length > 0 AndAlso Money.TryParse(_txtDp.Text, amount, problem) Then _txtDp.Text = Money.FormatPlain(amount)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        _txtName.Focus()
    End Sub

    Protected Overrides Async Function OnSubmitAsync() As Task(Of Boolean)
        Validator.ClearAll(Body)
        Dim p = BookingAdmin.CustomerNameProblem(_txtName.Text)
        If p IsNot Nothing Then Validator.SetError(_txtName, p)
        p = BookingAdmin.ContactProblem(_txtContact.Text)
        If p IsNot Nothing Then Validator.SetError(_txtContact, p)
        p = UserAdmin.EmailProblem(_txtEmail.Text)
        If p IsNot Nothing Then Validator.SetError(_txtEmail, p)
        Dim package = If(_package.SelectedIndex >= 0, _packages(_package.SelectedIndex), Nothing)
        If package Is Nothing Then Validator.SetError(_package, "Choose a package.")
        If _time.SelectedIndex < 0 Then Validator.SetError(_time, "Choose a time.")

        Dim schedule = _date.Value.Date + If(_time.SelectedIndex >= 0, _times(_time.SelectedIndex), TimeSpan.Zero)
        If _existing Is Nothing OrElse _existing.ScheduleAt <> schedule Then
            p = BookingAdmin.ScheduleProblem(schedule)
            If p IsNot Nothing Then Validator.SetError(_date, p)
        End If

        Dim dp As Decimal = 0D
        If _txtDp.Text.Trim().Length > 0 Then
            Dim problem As String = Nothing
            If Not Money.TryParse(_txtDp.Text, dp, problem) Then
                Validator.SetError(_txtDp, problem)
            ElseIf package IsNot Nothing AndAlso dp > package.Price Then
                Validator.SetError(_txtDp, $"Can't exceed the package price ({Money.Format(package.Price)}).")
            End If
        End If
        Dim method = If(_dpMethod.SelectedIndex >= 0, PaymentMethods.All(_dpMethod.SelectedIndex), "")
        If dp > 0D AndAlso method.Length = 0 Then Validator.SetError(_dpMethod, "Choose how the downpayment was paid.")
        If dp > 0D AndAlso PaymentMethods.IsEWallet(method) Then
            p = BookingAdmin.ReferenceProblem(_txtRef.Text)
            If p IsNot Nothing Then Validator.SetError(_txtRef, p)
        End If
        If Validator.HasErrors(Body) Then
            Shake()
            Validator.FocusFirstError(Body)
            Return False
        End If

        Dim b As New Booking With {
            .Id = If(_existing Is Nothing, 0, _existing.Id), .CustomerName = _txtName.Text, .ContactNo = _txtContact.Text,
            .CustomerEmail = _txtEmail.Text, .PackageId = package.Id, .ScheduleAt = schedule, .Downpayment = dp,
            .DownpaymentMethod = method, .DownpaymentRef = _txtRef.Text, .Notes = _txtNotes.Text}
        Dim markDone = _chkDone.Visible AndAlso _chkDone.Checked
        SetBusy(True, "Saving...")
        Dim result = Await Task.Run(Function() BookingAdmin.Save(b, markDone))
        SetBusy(False)
        Dim fields As New Dictionary(Of String, Control) From {
            {"name", _txtName}, {"contact", _txtContact}, {"email", _txtEmail}, {"package", _package}, {"date", _date},
            {"downpayment", _txtDp}, {"dpMethod", _dpMethod}, {"dpRef", _txtRef}, {"notes", _txtNotes}}
        If FormErrors.Show(result, fields) Then
            Shake()
            Return False
        End If
        SavedId = CInt(result.Value)
        MarkedDone = markDone
        Return True
    End Function

End Class
