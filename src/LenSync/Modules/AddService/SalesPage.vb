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
    ''' Add Service > Sales: issued receipts. View/reprint a receipt. Paid receipts are final: voiding is done in
    ''' Billing, before the payment is recorded (older receipts voided before that change still show as Voided).
    ''' </summary>
    Public Class SalesPage
        Inherits ModulePage

        Private Shared ReadOnly Ranges As SalesRange() = {SalesRange.Today, SalesRange.Last7Days, SalesRange.Last30Days, SalesRange.All}

        Private ReadOnly _search As New LsTextBox()
        Private ReadOnly _range As New LsChoice()
        Private ReadOnly _grid As New LsGrid()
        Private ReadOnly _btnView As New LsButton("View receipt", ButtonVariant.Secondary)
        Private ReadOnly _lblTotal As New Label()
        Private _sales As New List(Of SaleSummary)

        Public Sub New()
            MyBase.New("Sales", "Receipts issued in Add Service. Open one to view or reprint it.")
            _search.Placeholder = "Search receipt no., customer or booking no."
            _search.FieldName = "Search sales"
            _search.Icon = LineIcon.Search
            _range.SetItems({"Today", "7 days", "30 days", "All"})
            _range.AccessibleName = "Show sales from"
            _range.Width = Theme.Dpi(300)
            _grid.SolidStatuses.Clear()
            _grid.SolidStatuses.Add("Completed")
            ' Receipt no. must show "AR-2026-000123" in full at a 1080p/150% content width.
            _grid.AddTextColumn("receipt", "Receipt no.", 17)
            _grid.AddTextColumn("date", "Date", 15)
            _grid.AddTextColumn("customer", "Customer", 16)
            _grid.AddTextColumn("method", "Paid via", 9)
            _grid.AddTextColumn("paid", "Amount paid", 13, alignRight:=True)
            _grid.AddTextColumn("cashier", "Cashier", 18)
            _grid.AddStatusColumn("status", "Status", 12)
            _lblTotal.Font = Theme.Label
            _lblTotal.ForeColor = Theme.Slate
            _lblTotal.BackColor = Theme.White
            _lblTotal.TextAlign = ContentAlignment.MiddleLeft
            Controls.AddRange({_search, _range, _btnView, _grid, _lblTotal})

            AddHandler _search.TextChanged, Sub() Fill()
            AddHandler _range.SelectedIndexChanged, Sub() ReloadQuietly()
            AddHandler _grid.SelectionChanged, Sub() UpdateButtons()
            AddHandler _grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then ViewSelected()
            AddHandler _grid.KeyDown, Sub(s, e)
                                          If e.KeyCode = Keys.Enter Then
                                              e.Handled = True
                                              ViewSelected()
                                          End If
                                      End Sub
            AddHandler _btnView.Click, Sub() ViewSelected()
            _range.SelectedIndex = 0
            UpdateButtons()
        End Sub

        Protected Overrides ReadOnly Property RefreshOnShow As Boolean
            Get
                Return True
            End Get
        End Property

        Public Overrides Async Function ReloadAsync() As Task
            Dim range = Ranges(Math.Max(0, _range.SelectedIndex))
            Dim data = Await RunAsync(Function() Billing.ListSales(range), "Loading sales")
            If data Is Nothing Then Return
            _sales = data
            Fill()
        End Function

        Private Async Sub ReloadQuietly()
            If Not IsHandleCreated Then Return
            Await ReloadAsync()
        End Sub

        Private Sub Fill(Optional selectId As Integer = 0)
            Dim keepId = If(selectId <> 0, selectId, If(TryCast(_grid.SelectedItem, SaleSummary)?.Id, 0))
            Dim q = _search.Text.Trim()
            _grid.Rows.Clear()
            Dim shown = 0
            Dim collected = 0D
            For Each s In _sales
                If q.Length > 0 AndAlso Not (Display.Has(s.ReceiptNo, q) OrElse Display.Has(s.CustomerName, q) OrElse Display.Has(s.BookingNo, q)) Then Continue For
                Dim i = _grid.Rows.Add(s.ReceiptNo, Display.Stamp(s.CreatedAt), s.CustomerName, s.PaymentMethod, Money.Format(s.BalanceDue), s.CashierName, s.Status)
                _grid.Rows(i).Tag = s
                shown += 1
                If s.Status = "Completed" Then collected += s.BalanceDue
            Next
            If keepId <> 0 Then _grid.SelectWhere(Function(t) DirectCast(t, SaleSummary).Id = keepId)
            _grid.EmptyText = If(q.Length > 0, "No receipts match your search.", "No sales in this period yet.")
            _lblTotal.Text = If(shown = 0, "", $"{shown} receipt{If(shown = 1, "", "s")}  ·  {Money.Format(collected)} collected (voided sales not counted)")
            UpdateButtons()
        End Sub

        Private ReadOnly Property Selected As SaleSummary
            Get
                Return TryCast(_grid.SelectedItem, SaleSummary)
            End Get
        End Property

        Private Sub UpdateButtons()
            Dim s = Selected
            _btnView.Enabled = s IsNot Nothing
        End Sub

        Protected Overrides Sub LayoutContent(area As Rectangle)
            Dim bottom = LayoutToolbar(area, {_search, _range}, {_btnView}, 160, 340)
            Dim gridTop = bottom + Theme.Dpi(16)
            Dim footH = Theme.Label.Height + Theme.Dpi(12)
            _grid.Bounds = New Rectangle(area.Left, gridTop, area.Width, Math.Max(0, area.Bottom - gridTop - footH))
            _lblTotal.Bounds = New Rectangle(area.Left, _grid.Bottom + Theme.Dpi(4), area.Width, footH - Theme.Dpi(4))
            _grid.Columns("cashier").Visible = area.Width >= Theme.Dpi(840)
        End Sub

        Private Async Sub ViewSelected()
            Dim s = Selected
            If s Is Nothing Then Return
            Dim id = s.Id
            Dim receipt = Await RunAsync(Function() Billing.LoadReceipt(id), "Loading receipt")
            If receipt Is Nothing Then Return
            Using dlg As New ReceiptDialog(receipt, afterSale:=False, changeAmount:=0D)
                dlg.ShowCard()
            End Using
            Await ReloadAsync()
            Fill(id)
        End Sub

    End Class

End Namespace
