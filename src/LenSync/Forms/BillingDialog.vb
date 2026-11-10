Option Strict On
Option Explicit On
Option Infer On

Imports System.Globalization
Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.Models
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

''' <summary>
''' Bill a finished shoot: the booked package plus add-ons and "Other" lines on the left; totals, discount and the
''' payment (Cash / GCash / Maya) on the right. "Complete payment" confirms, then saves everything in one DB
''' transaction (Billing.CompleteSale). The caller then shows the receipt.
''' </summary>
Public Class BillingDialog
    Inherits LsFormDialog

    Private Const LeftW As Integer = 500
    Private Const RightW As Integer = 330
    Private Const ColGap As Integer = 30
    Private Const BodyW As Integer = LeftW + ColGap + RightW
    Private Const BodyH As Integer = 412
    Private Const RightX As Integer = LeftW + ColGap

    Private ReadOnly _booking As Booking
    Private ReadOnly _addons As List(Of PriceItem)
    Private ReadOnly _lines As New List(Of BillLine)
    Private _discount As Discount
    Private _dirty As Boolean

    Private ReadOnly _lblItems As Label
    Private ReadOnly _lblCount As New Label()
    Private ReadOnly _grid As New LsGrid()
    Private ReadOnly _addon As New LsSelect()
    Private ReadOnly _txtQty As New LsTextBox()
    Private ReadOnly _btnAdd As New LsButton("Add", ButtonVariant.Secondary)
    Private ReadOnly _btnOther As New LsButton("Other item...", ButtonVariant.Ghost)
    Private ReadOnly _btnRemove As New LsButton("Remove line", ButtonVariant.Ghost)
    Private ReadOnly _summary As New BillSummary()
    Private ReadOnly _btnDiscount As New LsButton("Add discount", ButtonVariant.Secondary)
    Private ReadOnly _method As New LsChoice()
    Private ReadOnly _lblAmount As Label
    Private ReadOnly _lblChange As New Label()
    Private ReadOnly _txtTendered As New LsTextBox()
    Private ReadOnly _txtRef As New LsTextBox()

    ''' <summary>Set when the payment was saved.</summary>
    Public Property Outcome As SaleOutcome

    Public Sub New(booking As Booking, addons As List(Of PriceItem))
        MyBase.New("Billing", booking.CustomerName,
                   $"{booking.BookingNo}  ·  {booking.PackageName}  ·  shoot {BookingAdmin.FormatWhen(booking.ScheduleAt)}",
                   "Complete payment", "Cancel", BodyW, BodyH)
        _booking = booking
        _addons = addons
        _lines.AddRange(Billing.InitialLines(booking))

        ' ---------------- left: items
        _lblItems = MakeLabel("Items", 0, 0, LeftW)
        _lblCount.Font = Theme.Label
        _lblCount.ForeColor = Theme.Slate
        _lblCount.TextAlign = ContentAlignment.BottomRight
        _lblCount.Bounds = New Rectangle(Theme.Dpi(LeftW \ 2), 0, Theme.Dpi(LeftW \ 2), _lblItems.Height)
        Body.Controls.Add(_lblCount)

        _grid.RowTemplate.Height = Theme.Dpi(38)
        _grid.EmptyText = "No items yet. Add the package or an add-on below."
        _grid.AddTextColumn("item", "Item", 44)
        _grid.AddTextColumn("qty", "Qty", 13, alignRight:=True)
        _grid.AddTextColumn("price", "Price", 21, alignRight:=True)
        _grid.AddTextColumn("amount", "Amount", 22, alignRight:=True)
        _grid.AccessibleName = "Items on this bill"
        _grid.Bounds = New Rectangle(0, Theme.Dpi(24), Theme.Dpi(LeftW), Theme.Dpi(232))
        Body.Controls.Add(_grid)

        Dim addY = Theme.Dpi(284)
        _addon.Placeholder = If(addons.Count > 0, "Choose an add-on", "No add-ons available")
        _addon.FieldName = "Add-on"
        _addon.SetItems(addons.Select(Function(a) a.Name), addons.Select(Function(a) Money.Format(a.Price)))
        _addon.Enabled = addons.Count > 0
        _addon.Bounds = New Rectangle(0, addY, Theme.Dpi(268), Theme.Dpi(40))
        _txtQty.FieldName = "Quantity"
        _txtQty.Filter = InputFilter.Digits
        _txtQty.MaxLength = 3
        _txtQty.Placeholder = "Qty"
        _txtQty.Bounds = New Rectangle(_addon.Right + Theme.Dpi(8), addY, Theme.Dpi(64), Theme.Dpi(40))
        _btnAdd.FitToText(84)
        _btnAdd.Location = New Point(_txtQty.Right + Theme.Dpi(8), addY + (Theme.Dpi(40) - _btnAdd.Height) \ 2)
        _btnAdd.Enabled = addons.Count > 0
        _btnOther.FitToText(110)
        _btnOther.Location = New Point(0, Theme.Dpi(348))
        _btnRemove.FitToText(110)
        _btnRemove.Location = New Point(_btnOther.Right + Theme.Dpi(8), Theme.Dpi(348))
        Body.Controls.AddRange({_addon, _txtQty, _btnAdd, _btnOther, _btnRemove})

        ' ---------------- right: totals and payment
        _summary.Bounds = New Rectangle(Theme.Dpi(RightX), 0, Theme.Dpi(RightW), Theme.Dpi(192))
        _btnDiscount.FitToText(140)
        _btnDiscount.Location = New Point(Theme.Dpi(RightX), Theme.Dpi(200))
        Body.Controls.AddRange({_summary, _btnDiscount})

        Dim lblMethod = MakeLabel("Paid via", RightX, 248, RightW)
        _method.SetItems(PaymentMethods.All)
        _method.AccessibleName = "Paid via"
        _method.Bounds = New Rectangle(Theme.Dpi(RightX), lblMethod.Bottom + Theme.Dpi(4), Theme.Dpi(RightW), Theme.Dpi(40))
        Body.Controls.Add(_method)

        _lblAmount = MakeLabel("Amount received (₱)", RightX, 318, RightW \ 2 + 30)
        _lblChange.Font = Theme.LabelBold
        _lblChange.ForeColor = Theme.Ink
        _lblChange.TextAlign = ContentAlignment.BottomRight
        _lblChange.Bounds = New Rectangle(Theme.Dpi(RightX + RightW \ 2), _lblAmount.Top, Theme.Dpi(RightW \ 2), _lblAmount.Height)
        Body.Controls.Add(_lblChange)
        Dim fieldTop = _lblAmount.Bottom + Theme.Dpi(4)
        _txtTendered.Filter = InputFilter.Money
        _txtTendered.MaxLength = 13
        _txtTendered.FieldName = "Amount received"
        _txtTendered.Bounds = New Rectangle(Theme.Dpi(RightX), fieldTop, Theme.Dpi(RightW), Theme.Dpi(40))
        _txtRef.MaxLength = 40
        _txtRef.FieldName = "Reference number"
        _txtRef.Placeholder = "From the GCash/Maya receipt"
        _txtRef.Bounds = _txtTendered.Bounds
        Body.Controls.AddRange({_txtTendered, _txtRef})

        AddHandler _grid.SelectionChanged, Sub() UpdateButtons()
        AddHandler _grid.KeyDown, Sub(s, e)
                                      If e.KeyCode = Keys.Delete Then
                                          e.Handled = True
                                          RemoveSelected()
                                      End If
                                  End Sub
        AddHandler _btnAdd.Click, Sub() AddAddon()
        AddHandler _btnOther.Click, Sub() AddOther()
        AddHandler _btnRemove.Click, Sub() RemoveSelected()
        AddHandler _btnDiscount.Click, Sub() EditDiscount()
        AddHandler _method.SelectedIndexChanged, Sub() OnMethodChanged()
        AddHandler _txtTendered.TextChanged, Sub()
                                                 _dirty = True
                                                 UpdateChange()
                                             End Sub
        AddHandler _txtTendered.InnerTextBox.Leave, AddressOf FormatTendered
        AddHandler _txtRef.TextChanged, Sub() _dirty = True

        _method.SelectedIndex = 0
        RefreshBill()
        _dirty = False   ' setting the defaults above isn't a user edit
    End Sub

    Private Function MakeLabel(caption As String, x96 As Integer, y96 As Integer, w96 As Integer) As Label
        Dim lbl As New Label() With {
            .Text = caption, .Font = Theme.LabelBold, .ForeColor = Theme.Charcoal, .TextAlign = ContentAlignment.BottomLeft,
            .AutoEllipsis = True, .Bounds = New Rectangle(Theme.Dpi(x96), Theme.Dpi(y96), Theme.Dpi(w96), Theme.LabelBold.Height + Theme.Dpi(4))}
        Body.Controls.Add(lbl)
        Return lbl
    End Function

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        If _addons.Count > 0 Then _addon.Focus() Else _txtTendered.Focus()
    End Sub

    ' ---------------------------------------------------------------- bill lines
    Private ReadOnly Property Method As String
        Get
            Return If(_method.SelectedIndex >= 0, PaymentMethods.All(_method.SelectedIndex), "")
        End Get
    End Property

    Private ReadOnly Property Totals As BillTotals
        Get
            Return Billing.Compute(_lines, _discount, _booking.Downpayment)
        End Get
    End Property

    Private Sub RefreshBill(Optional selectIndex As Integer = -1)
        Dim keep = If(selectIndex >= 0, selectIndex, If(_grid.CurrentRow Is Nothing, 0, _grid.CurrentRow.Index))
        _grid.Rows.Clear()
        For Each l In _lines
            Dim i = _grid.Rows.Add(l.Description, l.Qty.ToString(CultureInfo.InvariantCulture), Money.FormatPlain(l.UnitPrice), Money.FormatPlain(l.LineTotal))
            _grid.Rows(i).Tag = l
            If l.ItemType = ItemTypes.Package Then _grid.Rows(i).Cells("item").ToolTipText = "The booked package"
        Next
        If _lines.Count > 0 Then
            Dim index = Math.Max(0, Math.Min(_lines.Count - 1, keep))
            _grid.SelectWhere(Function(tag) tag Is _lines(index))
        End If
        _lblCount.Text = $"{_lines.Count} of {Billing.MaxLines} lines"
        Dim t = Totals
        Dim note = ""
        If _discount IsNot Nothing Then note = If(_discount.Kind = DiscountTypes.Percent, $"{_discount.Value:0.##}% · ", "") & _discount.Reason
        _summary.SetTotals(t, note)
        UpdateChange()
        UpdateButtons()
    End Sub

    Private Sub UpdateButtons()
        Dim selected = TryCast(_grid.SelectedItem, BillLine)
        _btnRemove.Enabled = selected IsNot Nothing AndAlso selected.ItemType <> ItemTypes.Package
        Dim full = _lines.Count >= Billing.MaxLines
        _btnOther.Enabled = Not full
    End Sub

    Private Sub AddAddon()
        Validator.Clear(_addon)
        Validator.Clear(_txtQty)
        If _addon.SelectedIndex < 0 Then
            Validator.SetError(_addon, "Choose an add-on first.")
            _addon.Focus()
            Return
        End If
        Dim qty = 1
        If _txtQty.Text.Trim().Length > 0 AndAlso Not Integer.TryParse(_txtQty.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, qty) Then qty = 0
        Dim item = _addons(_addon.SelectedIndex)
        Dim existing = _lines.FirstOrDefault(Function(l) l.ItemType = ItemTypes.Addon AndAlso l.RefId.HasValue AndAlso l.RefId.Value = item.Id)
        Dim p = Billing.QtyProblem(If(existing Is Nothing, qty, existing.Qty + qty))
        If p IsNot Nothing Then
            Validator.SetError(_txtQty, p)
            _txtQty.Focus()
            Return
        End If
        If existing Is Nothing AndAlso _lines.Count >= Billing.MaxLines Then
            Validator.SetError(_addon, $"A bill can have at most {Billing.MaxLines} lines.")
            Return
        End If
        If existing IsNot Nothing Then
            existing.Qty += qty
        Else
            existing = New BillLine With {.ItemType = ItemTypes.Addon, .RefId = item.Id, .Description = item.Name, .Qty = qty, .UnitPrice = item.Price}
            _lines.Add(existing)
        End If
        _dirty = True
        Validator.Clear(_grid)
        _txtQty.Text = ""
        RefreshBill(_lines.IndexOf(existing))
        _addon.Focus()
    End Sub

    Private Sub AddOther()
        If _lines.Count >= Billing.MaxLines Then Return
        Using dlg As New OtherItemDialog()
            If dlg.ShowCard(Me) <> DialogResult.OK Then Return
            _lines.Add(dlg.Line)
        End Using
        _dirty = True
        Validator.Clear(_grid)
        RefreshBill(_lines.Count - 1)
    End Sub

    Private Sub RemoveSelected()
        Dim selected = TryCast(_grid.SelectedItem, BillLine)
        If selected Is Nothing OrElse selected.ItemType = ItemTypes.Package Then Return
        Dim index = _lines.IndexOf(selected)
        _lines.Remove(selected)
        _dirty = True
        Validator.Clear(_grid)
        RefreshBill(Math.Max(0, index - 1))
    End Sub

    Private Sub EditDiscount()
        Dim subtotal = Totals.Subtotal
        If subtotal <= 0D Then
            Validator.SetError(_btnDiscount, "Add an item first.")
            Return
        End If
        Using dlg As New DiscountDialog(subtotal, _discount)
            If dlg.ShowCard(Me) <> DialogResult.OK Then Return
            _discount = dlg.Result
        End Using
        _dirty = True
        _btnDiscount.Text = If(_discount Is Nothing, "Add discount", "Change discount")
        _btnDiscount.FitToText(140)
        Validator.Clear(_btnDiscount)
        RefreshBill()
    End Sub

    ' ---------------------------------------------------------------- payment
    Private Sub OnMethodChanged()
        Dim ewallet = PaymentMethods.IsEWallet(Method)
        _txtTendered.Visible = Not ewallet
        _txtRef.Visible = ewallet
        _lblAmount.Text = If(ewallet, $"{Method} reference no.", "Amount received (₱)")
        Validator.Clear(_txtTendered)
        Validator.Clear(_txtRef)
        _dirty = True
        UpdateChange()
    End Sub

    Private Sub UpdateChange()
        Dim balance = Totals.BalanceDue
        If PaymentMethods.IsEWallet(Method) Then
            _lblChange.Text = "Exact amount"
            _txtTendered.Placeholder = ""
            Return
        End If
        _txtTendered.Placeholder = $"e.g. {Money.FormatPlain(balance)}"
        Dim tendered As Decimal
        Dim problem As String = Nothing
        If _txtTendered.Text.Trim().Length = 0 OrElse Not Money.TryParse(_txtTendered.Text, tendered, problem) Then
            _lblChange.Text = ""
        ElseIf tendered >= balance Then
            _lblChange.Text = "Change " & Money.Format(tendered - balance)
        Else
            _lblChange.Text = "Short by " & Money.Format(balance - tendered)
        End If
    End Sub

    Private Sub FormatTendered(sender As Object, e As EventArgs)
        Dim amount As Decimal
        Dim problem As String = Nothing
        If _txtTendered.Text.Trim().Length > 0 AndAlso Money.TryParse(_txtTendered.Text, amount, problem) Then _txtTendered.Text = Money.FormatPlain(amount)
    End Sub

    ''' <summary>Enter inside the add-on row adds the add-on instead of completing the payment.</summary>
    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        If keyData = Keys.Enter AndAlso Not IsBusy AndAlso (_addon.Focused OrElse _txtQty.ContainsFocus) Then
            AddAddon()
            Return True
        End If
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    Protected Overrides Sub OnSecondary()
        If IsBusy Then Return
        If _dirty AndAlso Not LsDialog.Confirm("The items and payment details you entered will be discarded. The booking stays unpaid.",
                                               "Discard this bill?", "Discard", "Keep billing") Then Return
        CloseCard(DialogResult.Cancel)
    End Sub

    Protected Overrides Async Function OnSubmitAsync() As Task(Of Boolean)
        Validator.ClearAll(Body)
        Dim t = Totals
        Dim m = Method
        Dim tendered As Decimal = 0D
        Dim reference = _txtRef.Text
        Dim fields As New Dictionary(Of String, Control) From {
            {"lines", _grid}, {"discount", _btnDiscount}, {"method", _method}, {"reference", _txtRef}, {"tendered", _txtTendered}}

        Dim check = Billing.ValidateBill(_lines, _discount, _booking.Downpayment)
        If m = PaymentMethods.Cash AndAlso _txtTendered.Text.Trim().Length > 0 Then
            Dim problem As String = Nothing
            If Not Money.TryParse(_txtTendered.Text, tendered, problem) Then check.AddError("tendered", problem)
        ElseIf m = PaymentMethods.Cash AndAlso t.BalanceDue > 0D Then
            check.AddError("tendered", "Enter the cash the customer handed over.")
        End If
        If Not check.Errors.Any(Function(entry) entry.Key = "tendered") Then
            For Each problemEntry In Billing.PaymentProblems(m, reference, tendered, t.BalanceDue).Errors
                check.AddError(problemEntry.Key, problemEntry.Value)
            Next
        End If
        If FormErrors.Show(check, fields) Then
            Shake()
            Validator.FocusFirstError(Body)
            Return False
        End If

        Dim prompt As String
        If PaymentMethods.IsEWallet(m) Then
            prompt = $"Record {Money.Format(t.BalanceDue)} paid by {m} (reference {BookingAdmin.NormalizeReference(reference)}) for {_booking.CustomerName}."
        ElseIf t.BalanceDue = 0D Then
            prompt = $"The downpayment already covers the total of {Money.Format(t.Total)}. Close this bill as paid?"
        Else
            prompt = $"Collect {Money.Format(t.BalanceDue)} in cash from {_booking.CustomerName}. Received {Money.Format(tendered)}, change {Money.Format(tendered - t.BalanceDue)}."
        End If
        If Not LsDialog.Confirm(prompt, "Complete this payment?", "Complete payment", "Go back") Then Return False

        Dim request As New SaleRequest With {
            .BookingId = _booking.Id, .Lines = _lines.Select(Function(l) l.Clone()).ToList(), .Discount = _discount,
            .PaymentMethod = m, .ReferenceNo = reference, .AmountTendered = tendered, .ExpectedBalance = t.BalanceDue}
        SetBusy(True, "Saving...")
        Dim result = Await Task.Run(Function() Billing.CompleteSale(request))
        SetBusy(False)
        If FormErrors.Show(result, fields) Then
            Shake()
            Return False
        End If
        Outcome = DirectCast(result.Value, SaleOutcome)
        Return True
    End Function

End Class
