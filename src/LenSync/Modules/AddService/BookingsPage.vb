Option Strict On
Option Explicit On
Option Infer On

Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.Models
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

Namespace Modules.AddService

    ''' <summary>
    ''' Add Service > Bookings: schedule shoots and record downpayments. "Shoot done" moves a booking to Billing;
    ''' on a booking that's already done the same button reads "Bill now".
    ''' </summary>
    Public Class BookingsPage
        Inherits ModulePage

        Private Shared ReadOnly Filters As BookingFilter() = {BookingFilter.Open, BookingFilter.Paid, BookingFilter.Cancelled, BookingFilter.All}

        Private ReadOnly _search As New LsTextBox()
        Private ReadOnly _filter As New LsChoice()
        Private ReadOnly _grid As New LsGrid()
        Private ReadOnly _btnNew As New LsButton("New booking", ButtonVariant.Primary)
        Private ReadOnly _btnEdit As New LsButton("Edit", ButtonVariant.Secondary)
        Private ReadOnly _btnNext As New LsButton("Shoot done", ButtonVariant.Secondary)
        Private ReadOnly _btnCancel As New LsButton("Cancel booking", ButtonVariant.Secondary)
        Private _bookings As New List(Of Booking)
        Private _busy As Boolean

        Public Sub New()
            MyBase.New("Bookings", "Schedule shoots and record downpayments. Mark a shoot done when it's finished so it can be billed.")
            AddAction(_btnNew)
            _search.Placeholder = "Search bookings"
            _search.FieldName = "Search bookings"
            _search.Icon = LineIcon.Search
            _filter.SetItems({"Open", "Paid", "Cancelled", "All"})
            _filter.AccessibleName = "Show bookings"
            _filter.Width = Theme.Dpi(300)
            _btnEdit.FitToText(90)
            _btnNext.FitToText(120)
            _grid.EmptyText = "No bookings to show."
            _grid.SolidStatuses.Clear()
            _grid.SolidStatuses.Add(BookingStatus.Unpaid)
            _grid.AddTextColumn("no", "Booking no.", 14)
            _grid.AddTextColumn("customer", "Customer", 21)
            _grid.AddTextColumn("package", "Package", 18)
            _grid.AddTextColumn("schedule", "Schedule", 19)
            _grid.AddTextColumn("dp", "Downpayment", 14, alignRight:=True)
            _grid.AddStatusColumn("status", "Status", 12)
            Controls.AddRange({_search, _filter, _btnEdit, _btnNext, _btnCancel, _grid})

            AddHandler _search.TextChanged, Sub() Fill()
            AddHandler _filter.SelectedIndexChanged, Sub() ReloadQuietly()
            AddHandler _grid.SelectionChanged, Sub() UpdateButtons()
            AddHandler _grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then EditSelected()
            AddHandler _grid.KeyDown, Sub(s, e)
                                          If e.KeyCode = Keys.Enter Then
                                              e.Handled = True
                                              EditSelected()
                                          End If
                                      End Sub
            AddHandler _btnNew.Click, Sub() CreateBooking()
            AddHandler _btnEdit.Click, Sub() EditSelected()
            AddHandler _btnNext.Click, Sub() NextStep()
            AddHandler _btnCancel.Click, Sub() CancelSelected()
            _filter.SelectedIndex = 0
            UpdateButtons()
        End Sub

        ' Bookings move to Billing/Sales from other tabs, so reload whenever this tab is shown.
        Protected Overrides ReadOnly Property RefreshOnShow As Boolean
            Get
                Return True
            End Get
        End Property

        Public Overrides Async Function ReloadAsync() As Task
            Dim filter = Filters(Math.Max(0, _filter.SelectedIndex))
            Dim data = Await RunAsync(Function() BookingAdmin.List(filter), "Loading bookings")
            If data Is Nothing Then Return
            _bookings = data
            Fill()
        End Function

        Private Async Sub ReloadQuietly()
            If Not IsHandleCreated Then Return
            Await ReloadAsync()
        End Sub

        Private Sub Fill(Optional selectId As Integer = 0)
            Dim keepId = If(selectId <> 0, selectId, If(Selected?.Id, 0))
            Dim q = _search.Text.Trim()
            _grid.Rows.Clear()
            For Each b In _bookings
                If q.Length > 0 AndAlso Not (Display.Has(b.CustomerName, q) OrElse Display.Has(b.BookingNo, q) OrElse
                                             Display.Has(b.ContactNo, q) OrElse Display.Has(b.PackageName, q)) Then Continue For
                Dim dp = If(b.Downpayment > 0D, Money.Format(b.Downpayment), "—")
                Dim i = _grid.Rows.Add(b.BookingNo, b.CustomerName, If(b.PackageName.Length > 0, b.PackageName, "—"),
                                       Display.Schedule(b.ScheduleAt), dp, b.Status)
                _grid.Rows(i).Tag = b
                If b.Downpayment > 0D Then
                    _grid.Rows(i).Cells("dp").ToolTipText = $"Paid via {b.DownpaymentMethod}" & If(b.DownpaymentRef.Length > 0, $", ref {b.DownpaymentRef}", "")
                End If
                If b.Notes.Length > 0 Then _grid.Rows(i).Cells("customer").ToolTipText = b.Notes
            Next
            If keepId <> 0 Then _grid.SelectWhere(Function(t) DirectCast(t, Booking).Id = keepId)
            _grid.EmptyText = If(q.Length > 0, "No bookings match your search.",
                                 If(_filter.SelectedIndex = 0, "No open bookings. Click New booking to schedule a shoot.", "No bookings to show."))
            UpdateButtons()
        End Sub

        Private ReadOnly Property Selected As Booking
            Get
                Return TryCast(_grid.SelectedItem, Booking)
            End Get
        End Property

        Private Sub UpdateButtons()
            Dim b = Selected
            Dim editable = b IsNot Nothing AndAlso b.IsEditable AndAlso Not _busy
            _btnEdit.Enabled = editable
            _btnCancel.Enabled = editable
            _btnNext.Enabled = editable
            _btnNext.Text = If(b IsNot Nothing AndAlso b.Status = BookingStatus.Unpaid, "Bill now", "Shoot done")
            _btnNew.Enabled = Not _busy
        End Sub

        Protected Overrides Sub LayoutContent(area As Rectangle)
            Dim bottom = LayoutToolbar(area, {_search, _filter}, {_btnCancel, _btnNext, _btnEdit}, 150, 320)
            Dim gridTop = bottom + Theme.Dpi(16)
            _grid.Bounds = New Rectangle(area.Left, gridTop, area.Width, Math.Max(0, area.Bottom - gridTop))
            ' Narrow windows drop the Package column so the rest stays readable.
            _grid.Columns("package").Visible = area.Width >= Theme.Dpi(820)
        End Sub

        ' ---------------------------------------------------------------- actions
        Private Async Function LoadPackagesAsync() As Task(Of List(Of PriceItem))
            Dim packages = Await RunAsync(Function() BookingAdmin.ActivePackages(), "Loading packages")
            If packages IsNot Nothing AndAlso packages.Count = 0 Then
                LsDialog.Warning("There are no active packages yet. Ask the Admin to add one in Settings > Packages.", "No packages")
                Return Nothing
            End If
            Return packages
        End Function

        Private Async Sub CreateBooking()
            If _busy Then Return
            Dim packages = Await LoadPackagesAsync()
            If packages Is Nothing Then Return
            Dim savedId As Integer
            Dim done As Boolean
            Using dlg As New BookingDialog(packages)
                If dlg.ShowCard() <> DialogResult.OK Then Return
                savedId = dlg.SavedId
                done = dlg.MarkedDone
            End Using
            AppEvents.RaiseSalesChanged()
            _filter.SelectedIndex = 0
            Await ReloadAsync()
            Fill(savedId)
            Dim b = Selected
            Toast.Show(If(b Is Nothing, "Booking saved.", $"{b.BookingNo} saved for {b.CustomerName}."), ToastKind.Success)
            If done Then Await BillAsync(savedId)
        End Sub

        Private Async Sub EditSelected()
            Dim b = Selected
            If b Is Nothing OrElse Not b.IsEditable OrElse _busy Then Return
            Dim packages = Await LoadPackagesAsync()
            If packages Is Nothing Then Return
            Dim done As Boolean
            Using dlg As New BookingDialog(packages, b)
                If dlg.ShowCard() <> DialogResult.OK Then Return
                done = dlg.MarkedDone
            End Using
            AppEvents.RaiseSalesChanged()
            Await ReloadAsync()
            Fill(b.Id)
            Toast.Show($"{b.BookingNo} saved.", ToastKind.Success)
            If done Then Await BillAsync(b.Id)
        End Sub

        Private Async Sub NextStep()
            Dim b = Selected
            If b Is Nothing OrElse _busy Then Return
            If b.Status = BookingStatus.Unpaid Then
                Await BillAsync(b.Id)
                Return
            End If
            If b.Status <> BookingStatus.Pending Then Return
            If b.ScheduleAt.Date > Date.Today AndAlso Not LsDialog.Confirm(
                $"{b.BookingNo} is scheduled for {Display.Schedule(b.ScheduleAt)}. Mark the shoot as done now?",
                "Shoot not due yet", "Mark as done", "Keep pending") Then Return
            Dim id = b.Id
            Dim result = Await RunAsync(Function() BookingAdmin.MarkDone(id), "Marking shoot done")
            If result Is Nothing OrElse FormErrors.Show(result, Nothing) Then Return
            AppEvents.RaiseSalesChanged()
            Await ReloadAsync()
            Fill(id)
            Toast.Show($"{b.BookingNo} is ready for billing.", ToastKind.Success)
        End Sub

        Private Async Function BillAsync(bookingId As Integer) As Task
            _busy = True
            UpdateButtons()
            Try
                Await BillingFlow.RunAsync(Me, bookingId)
            Finally
                _busy = False
            End Try
            Await ReloadAsync()
            Fill(bookingId)
        End Function

        Private Async Sub CancelSelected()
            Dim b = Selected
            If b Is Nothing OrElse Not b.IsEditable OrElse _busy Then Return
            Dim id = b.Id
            Dim dpNote = If(b.Downpayment > 0D, $" The {Money.Format(b.Downpayment)} downpayment stays on record; refund it according to studio policy.", "")
            Using dlg As New ReasonDialog("Cancel booking", $"Cancel {b.BookingNo}?",
                                          $"{b.CustomerName}'s booking will be closed and can't be billed.{dpNote}",
                                          "Cancel booking", "e.g. Customer rescheduled to next month",
                                          Function(reason) BookingAdmin.Cancel(id, reason))
                If dlg.ShowCard() <> DialogResult.OK Then Return
            End Using
            AppEvents.RaiseSalesChanged()
            Await ReloadAsync()
            Toast.Show($"{b.BookingNo} was cancelled.", ToastKind.Success)
        End Sub

    End Class

End Namespace
