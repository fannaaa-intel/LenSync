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
    ''' Add Service > Billing: shoots that are done and waiting for payment. Select one and bill it, or void the
    ''' unpaid bill (VOID_SALE only, reason required) before any payment is recorded.
    ''' </summary>
    Public Class BillingPage
        Inherits ModulePage

        Private ReadOnly _search As New LsTextBox()
        Private ReadOnly _grid As New LsGrid()
        Private ReadOnly _btnBill As New LsButton("Bill this booking", ButtonVariant.Primary)
        Private ReadOnly _btnVoid As New LsButton("Void bill", ButtonVariant.Secondary)
        Private _bookings As New List(Of Booking)
        Private _busy As Boolean

        Public Sub New()
            MyBase.New("Billing", "Finished shoots waiting for payment. Add extras and discounts, take the payment, then print the receipt.")
            _search.Placeholder = "Search name or booking no."
            _search.FieldName = "Search unpaid bookings"
            _search.Icon = LineIcon.Search
            _grid.AddTextColumn("no", "Booking no.", 14)
            _grid.AddTextColumn("customer", "Customer", 21)
            _grid.AddTextColumn("package", "Package", 18)
            _grid.AddTextColumn("shoot", "Shoot", 23)
            _grid.AddTextColumn("price", "Package price", 13, alignRight:=True)
            _grid.AddTextColumn("dp", "Downpayment", 13, alignRight:=True)
            _btnVoid.Visible = Session.HasPermission(Permissions.VoidSale)
            Controls.AddRange({_search, _btnBill, _btnVoid, _grid})

            AddHandler _search.TextChanged, Sub() Fill()
            AddHandler _grid.SelectionChanged, Sub() UpdateButtons()
            AddHandler _grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then BillSelected()
            AddHandler _grid.KeyDown, Sub(s, e)
                                          If e.KeyCode = Keys.Enter Then
                                              e.Handled = True
                                              BillSelected()
                                          End If
                                      End Sub
            AddHandler _btnBill.Click, Sub() BillSelected()
            AddHandler _btnVoid.Click, Sub() VoidSelected()
            UpdateButtons()
        End Sub

        Protected Overrides ReadOnly Property RefreshOnShow As Boolean
            Get
                Return True
            End Get
        End Property

        Public Overrides Async Function ReloadAsync() As Task
            Dim data = Await RunAsync(Function() Billing.ListUnpaid(), "Loading unpaid bookings")
            If data Is Nothing Then Return
            _bookings = data
            Fill()
        End Function

        Private Sub Fill()
            Dim keepId = If(TryCast(_grid.SelectedItem, Booking)?.Id, 0)
            Dim q = _search.Text.Trim()
            _grid.Rows.Clear()
            For Each b In _bookings
                If q.Length > 0 AndAlso Not (Display.Has(b.CustomerName, q) OrElse Display.Has(b.BookingNo, q) OrElse Display.Has(b.ContactNo, q)) Then Continue For
                Dim i = _grid.Rows.Add(b.BookingNo, b.CustomerName, If(b.PackageName.Length > 0, b.PackageName, "—"), Display.Schedule(b.ScheduleAt),
                                       Money.Format(b.PackagePrice), If(b.Downpayment > 0D, Money.Format(b.Downpayment), "—"))
                _grid.Rows(i).Tag = b
            Next
            If keepId <> 0 Then _grid.SelectWhere(Function(t) DirectCast(t, Booking).Id = keepId)
            _grid.EmptyText = If(q.Length > 0, "No unpaid bookings match your search.",
                                 "Nothing to bill. When a shoot is finished, click Shoot done on the Bookings tab.")
            UpdateButtons()
        End Sub

        Private Sub UpdateButtons()
            _btnBill.Enabled = Not _busy AndAlso TypeOf _grid.SelectedItem Is Booking
            _btnVoid.Enabled = _btnBill.Enabled
        End Sub

        Protected Overrides Sub LayoutContent(area As Rectangle)
            Dim bottom = LayoutToolbar(area, {_search}, {_btnBill, _btnVoid}, 160, 360)
            Dim gridTop = bottom + Theme.Dpi(16)
            _grid.Bounds = New Rectangle(area.Left, gridTop, area.Width, Math.Max(0, area.Bottom - gridTop))
            _grid.Columns("package").Visible = area.Width >= Theme.Dpi(820)
        End Sub

        Private Async Sub BillSelected()
            Dim b = TryCast(_grid.SelectedItem, Booking)
            If b Is Nothing OrElse _busy Then Return
            _busy = True
            UpdateButtons()
            Try
                Await BillingFlow.RunAsync(Me, b.Id)
            Finally
                _busy = False
            End Try
            Await ReloadAsync()
        End Sub

        Private Async Sub VoidSelected()
            Dim b = TryCast(_grid.SelectedItem, Booking)
            If b Is Nothing OrElse _busy OrElse Not Session.HasPermission(Permissions.VoidSale) Then Return
            Dim id = b.Id
            Dim dpNote = If(b.Downpayment > 0D, $" The {Money.Format(b.Downpayment)} downpayment stays on record.", "")
            Using dlg As New ReasonDialog("Void bill", $"Void the bill for {b.CustomerName}?",
                                          $"No payment is recorded and no receipt is issued. {b.BookingNo} is closed as Voided and leaves Billing.{dpNote} This can't be undone.",
                                          "Void bill", "e.g. Customer declined the service",
                                          Function(reason) Billing.VoidBill(id, reason))
                If dlg.ShowCard() <> DialogResult.OK Then Return
            End Using
            AppEvents.RaiseSalesChanged()
            Await ReloadAsync()
            Toast.Show($"The bill for {b.BookingNo} was voided. No receipt was issued.", ToastKind.Success)
        End Sub

    End Class

End Namespace
