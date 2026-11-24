Option Strict On
Option Explicit On
Option Infer On

Imports LenSync.Data
Imports LenSync.Models

Namespace Core

    ''' <summary>
    ''' Add Service > Billing and Sales. Money rules:
    ''' Tax = (sum(items) - discount) x tax rate; Total = sum(items) - discount + tax;
    ''' Balance due = Total - downpayment; Change = Tendered - Balance due.
    ''' E-wallets (GCash/Maya) need a reference number and pay exactly the balance (change 0).
    ''' Completing a sale is ONE DB transaction: receipt number, sale, items, booking -> Paid and the audit entries.
    ''' A bill can be voided only BEFORE it is paid (VoidBill, VOID_SALE); completed receipts are final.
    ''' </summary>
    Public NotInheritable Class Billing

        Private Sub New()
        End Sub

        ''' <summary>Keeps every bill on one half-letter receipt; repeated extras use the quantity instead.</summary>
        Public Const MaxLines As Integer = 12
        Public Const MaxQty As Integer = 999

        ' ================================================================ math (pure, no database)
        Public Shared Function DiscountAmountFor(subtotal As Decimal, d As Discount) As Decimal
            If d Is Nothing OrElse d.Value <= 0D Then Return 0D
            If d.Kind = DiscountTypes.Percent Then Return Decimal.Round(subtotal * d.Value / 100D, 2, MidpointRounding.AwayFromZero)
            Return d.Value
        End Function

        ''' <summary>Tax on the discounted amount, rounded to the centavo. <paramref name="ratePercent"/>: 6 = 6%.</summary>
        Public Shared Function TaxAmountFor(taxable As Decimal, ratePercent As Decimal) As Decimal
            If taxable <= 0D OrElse ratePercent <= 0D Then Return 0D
            Return Decimal.Round(taxable * ratePercent / 100D, 2, MidpointRounding.AwayFromZero)
        End Function

        Public Shared Function Compute(lines As IEnumerable(Of BillLine), d As Discount, downpayment As Decimal, taxRate As Decimal) As BillTotals
            Dim subtotal = lines.Sum(Function(l) l.LineTotal)
            Dim discountAmount = Math.Min(DiscountAmountFor(subtotal, d), subtotal)
            Dim taxable = subtotal - discountAmount
            Dim rate = Math.Max(0D, taxRate)
            Dim tax = TaxAmountFor(taxable, rate)
            Dim total = taxable + tax
            Return New BillTotals With {.Subtotal = subtotal, .DiscountAmount = discountAmount, .TaxRate = rate, .TaxAmount = tax,
                                        .Total = total, .Downpayment = downpayment, .BalanceDue = total - downpayment}
        End Function

        ''' <summary>"6%", "12.5%".</summary>
        Public Shared Function FormatRate(ratePercent As Decimal) As String
            Return ratePercent.ToString("0.##", Globalization.CultureInfo.InvariantCulture) & "%"
        End Function

        ''' <summary>The studio's tax rate (Settings > Studio). Missing or unreadable = 0 (no tax).</summary>
        Public Shared Function CurrentTaxRate() As Decimal
            Dim rate As Decimal
            If Not Decimal.TryParse(AppSettings.GetValue(AppSettings.TaxRate, "0"), Globalization.NumberStyles.Number,
                                    Globalization.CultureInfo.InvariantCulture, rate) Then Return 0D
            Return If(rate < 0D OrElse rate > MaxTaxRate, 0D, rate)
        End Function

        Public Const MaxTaxRate As Decimal = 100D

        ' ================================================================ validation (also used by the dialogs)
        Public Shared Function DiscountValueProblem(kind As String, value As Decimal, subtotal As Decimal) As String
            If value <= 0D Then Return "Enter the discount."
            If kind = DiscountTypes.Percent Then
                If value > 100D Then Return "A percent discount can be at most 100%."
            ElseIf value > subtotal Then
                Return $"It can't be more than the subtotal ({Money.Format(subtotal)})."
            End If
            Return Nothing
        End Function

        Public Shared Function OtherDescriptionProblem(description As String) As String
            description = If(description, "").Trim()
            If description.Length < 2 Then Return "Describe the item, e.g. Photo restoration."
            If description.Length > 255 Then Return "Use at most 255 characters."
            Return Nothing
        End Function

        Public Shared Function QtyProblem(qty As Integer) As String
            If qty < 1 OrElse qty > MaxQty Then Return $"Use a quantity from 1 to {MaxQty}."
            Return Nothing
        End Function

        ''' <summary>Checks the items, the discount and that the total still covers the downpayment.</summary>
        Public Shared Function ValidateBill(lines As IList(Of BillLine), d As Discount, downpayment As Decimal, taxRate As Decimal) As AdminResult
            Dim result As New AdminResult()
            If lines.Count = 0 Then
                result.AddError("lines", "Add at least one item.")
                Return result
            End If
            If lines.Count > MaxLines Then result.AddError("lines", $"A bill can have at most {MaxLines} lines. Use the quantity for repeated items.")
            For Each l In lines
                Dim p = QtyProblem(l.Qty)
                If p Is Nothing AndAlso (l.UnitPrice < 0D OrElse l.UnitPrice > Money.MaxAmount) Then p = "An item has an invalid price."
                If p Is Nothing AndAlso l.ItemType = ItemTypes.Other Then
                    p = OtherDescriptionProblem(l.Description)
                    If p Is Nothing AndAlso l.UnitPrice <= 0D Then p = $"'{l.Description}' needs a price."
                End If
                If p IsNot Nothing Then
                    result.AddError("lines", p)
                    Exit For
                End If
            Next
            Dim totals = Compute(lines, d, downpayment, taxRate)
            If totals.Subtotal > Money.MaxAmount OrElse totals.Total > Money.MaxAmount Then
                result.AddError("lines", $"The bill can't be more than {Money.Format(Money.MaxAmount)}.")
            End If
            If d IsNot Nothing Then
                Dim p = DiscountValueProblem(d.Kind, d.Value, totals.Subtotal)
                If p Is Nothing Then p = BookingAdmin.ReasonProblem(d.Reason)
                If p IsNot Nothing Then result.AddError("discount", p)
            End If
            If result.Ok AndAlso totals.BalanceDue < 0D Then
                result.AddError(If(d Is Nothing, "lines", "discount"),
                                $"The total ({Money.Format(totals.Total)}) is less than the downpayment already paid ({Money.Format(downpayment)}).")
            End If
            Return result
        End Function

        Public Shared Function PaymentProblems(method As String, reference As String, tendered As Decimal, balanceDue As Decimal) As AdminResult
            Dim result As New AdminResult()
            If Not PaymentMethods.All.Contains(method) Then
                result.AddError("method", "Choose how the customer paid.")
            ElseIf PaymentMethods.IsEWallet(method) Then
                If balanceDue > 0D Then
                    Dim p = BookingAdmin.ReferenceProblem(reference)
                    If p IsNot Nothing Then result.AddError("reference", p)
                End If
            ElseIf tendered > Money.MaxAmount Then
                result.AddError("tendered", "Enter a valid amount.")
            ElseIf tendered < balanceDue Then
                result.AddError("tendered", $"That's less than the balance due ({Money.Format(balanceDue)}).")
            End If
            Return result
        End Function

        ' ================================================================ queries
        Public Shared Function ListUnpaid() As List(Of Booking)
            Session.Demand(Permissions.AddService)
            Return BookingRepository.ListUnpaid()
        End Function

        ''' <summary>The booking to bill (must still be Unpaid), the active add-ons to choose from and the tax rate.</summary>
        Public Shared Function StartBill(bookingId As Integer) As BillStart
            Session.Demand(Permissions.AddService)
            Return New BillStart With {
                .Booking = BookingRepository.GetById(bookingId),
                .Addons = PriceItemRepository.List(PriceItemKind.Addon).Where(Function(a) a.IsActive).ToList(),
                .TaxRate = CurrentTaxRate()}
        End Function

        ''' <summary>The bill starts with the booked package at its current price.</summary>
        Public Shared Function InitialLines(b As Booking) As List(Of BillLine)
            Dim lines As New List(Of BillLine)
            If b IsNot Nothing AndAlso b.PackageId.HasValue Then
                lines.Add(New BillLine With {.ItemType = ItemTypes.Package, .RefId = b.PackageId, .Description = b.PackageName,
                                             .Qty = 1, .UnitPrice = b.PackagePrice})
            End If
            Return lines
        End Function

        Public Shared Function ListSales(range As SalesRange) As List(Of SaleSummary)
            Session.Demand(Permissions.AddService)
            Dim from As DateTime? = Nothing
            Select Case range
                Case SalesRange.Today : from = DateTime.Today
                Case SalesRange.Last7Days : from = DateTime.Today.AddDays(-6)
                Case SalesRange.Last30Days : from = DateTime.Today.AddDays(-29)
            End Select
            Return TransactionRepository.List(from)
        End Function

        ''' <summary>A receipt with the studio's current details, or Nothing if the sale doesn't exist.</summary>
        Public Shared Function LoadReceipt(transactionId As Integer) As Receipt
            Session.Demand(Permissions.AddService)
            Dim r = TransactionRepository.GetReceipt(transactionId)
            If r Is Nothing Then Return Nothing
            r.BusinessName = AppSettings.GetValue(AppSettings.BusinessName, "LENSYNC Photo Studio")
            r.Address = AppSettings.GetValue(AppSettings.Address)
            r.Contact = AppSettings.GetValue(AppSettings.Contact)
            r.LogoPath = AppSettings.GetValue(AppSettings.LogoPath)
            Return r
        End Function

        ' ================================================================ complete a sale
        Public Shared Function CompleteSale(req As SaleRequest) As AdminResult
            Session.Demand(Permissions.AddService)
            If req Is Nothing OrElse req.Lines Is Nothing Then Return AdminResult.Fail("lines", "Add at least one item.")
            Dim booking = BookingRepository.GetById(req.BookingId)
            If booking Is Nothing Then Return AdminResult.Fail("", "This booking no longer exists.")
            If booking.Status <> BookingStatus.Unpaid Then
                Return AdminResult.Fail("", $"This booking is {booking.Status.ToLowerInvariant()}, so it can't be billed. Refresh the list.")
            End If

            ' Catalogue lines are re-priced from the database: a bill can never change a package or add-on price.
            Dim lines As New List(Of BillLine)
            For Each source In req.Lines
                Dim l = source.Clone()
                l.Description = If(l.Description, "").Trim()
                Select Case l.ItemType
                    Case ItemTypes.Package, ItemTypes.Addon
                        Dim kind = If(l.ItemType = ItemTypes.Package, PriceItemKind.Package, PriceItemKind.Addon)
                        Dim item = If(l.RefId.HasValue, PriceItemRepository.GetById(kind, l.RefId.Value), Nothing)
                        If item Is Nothing Then Return AdminResult.Fail("lines", $"'{l.Description}' no longer exists. Remove it from the bill.")
                        l.Description = item.Name
                        l.UnitPrice = item.Price
                    Case ItemTypes.Other
                        l.RefId = Nothing
                    Case Else
                        Return AdminResult.Fail("lines", "The bill has an item of an unknown type.")
                End Select
                lines.Add(l)
            Next

            Dim d = req.Discount
            If d IsNot Nothing Then
                d = New Discount With {.Kind = d.Kind, .Value = d.Value, .Reason = If(d.Reason, "").Trim()}
                If d.Kind <> DiscountTypes.Fixed AndAlso d.Kind <> DiscountTypes.Percent Then Return AdminResult.Fail("discount", "Choose a discount type.")
            End If
            Dim method = If(req.PaymentMethod, "")
            Dim reference = If(PaymentMethods.IsEWallet(method), BookingAdmin.NormalizeReference(req.ReferenceNo), "")

            ' The rate is read here, not taken from the request: a bill can't choose its own tax.
            Dim taxRate = CurrentTaxRate()
            Dim result = ValidateBill(lines, d, booking.Downpayment, taxRate)
            Dim totals = Compute(lines, d, booking.Downpayment, taxRate)
            If result.Ok Then
                For Each e In PaymentProblems(method, reference, req.AmountTendered, totals.BalanceDue).Errors
                    result.AddError(e.Key, e.Value)
                Next
            End If
            If Not result.Ok Then Return result
            If totals.BalanceDue <> req.ExpectedBalance Then
                Return AdminResult.Fail("", "Prices, the tax rate or the booking changed while this bill was open. Close it and bill the booking again to see the new amounts.")
            End If
            Dim tendered = If(PaymentMethods.IsEWallet(method), totals.BalanceDue, req.AmountTendered)
            Dim change = tendered - totals.BalanceDue
            If d IsNot Nothing AndAlso totals.DiscountAmount = 0D Then d = Nothing

            Return Db.InTransaction(Of AdminResult)(
                Function(conn, tx)
                    Dim current = BookingRepository.GetForUpdate(conn, tx, booking.Id)
                    If current Is Nothing OrElse current.Status <> BookingStatus.Unpaid OrElse current.Downpayment <> booking.Downpayment Then
                        Return AdminResult.Fail("", "Someone else just billed or changed this booking. Refresh the list and check it.")
                    End If
                    Dim receiptNo = Db.NextDocumentNo(conn, tx, "AR", 6)
                    Dim txId = TransactionRepository.Insert(conn, tx, receiptNo, booking.Id, totals, d, method, reference, tendered, change, Session.CurrentUserId)
                    For Each l In lines
                        TransactionRepository.InsertItem(conn, tx, txId, l)
                    Next
                    BookingRepository.SetStatus(conn, tx, booking.Id, BookingStatus.Paid)
                    AuditLog.Write(conn, tx, AuditActions.SaleComplete, "transactions", txId,
                                   $"{receiptNo} for {booking.BookingNo} ({booking.CustomerName}): total {Money.Format(totals.Total)}" &
                                   If(totals.TaxAmount > 0D, $" incl. tax {FormatRate(totals.TaxRate)} {Money.Format(totals.TaxAmount)}", "") & ", " &
                                   $"downpayment {Money.Format(totals.Downpayment)}, paid {Money.Format(totals.BalanceDue)} via {method}" &
                                   If(reference.Length > 0, $" (ref {reference})", "") &
                                   If(method = PaymentMethods.Cash, $", received {Money.Format(tendered)}, change {Money.Format(change)}", ""))
                    If d IsNot Nothing Then
                        AuditLog.Write(conn, tx, AuditActions.Discount, "transactions", txId,
                                       $"{receiptNo}: {DescribeDiscount(d)} = -{Money.Format(totals.DiscountAmount)} on {Money.Format(totals.Subtotal)}. Reason: {d.Reason}")
                    End If
                    Return AdminResult.Success(New SaleOutcome With {.TransactionId = txId, .ReceiptNo = receiptNo, .ChangeAmount = change})
                End Function)
        End Function

        Public Shared Function DescribeDiscount(d As Discount) As String
            If d Is Nothing Then Return ""
            Return If(d.Kind = DiscountTypes.Percent, $"{d.Value:0.##}% discount", $"{Money.Format(d.Value)} discount")
        End Function

        ' ================================================================ void / print
        ''' <summary>
        ''' Voids an UNPAID bill (VOID_SALE only), before any payment is recorded: the booking becomes Voided with the
        ''' reason, no receipt is issued, and it leaves Billing. Paid receipts can't be voided.
        ''' </summary>
        Public Shared Function VoidBill(bookingId As Integer, reason As String) As AdminResult
            Session.Demand(Permissions.VoidSale)
            reason = If(reason, "").Trim()
            Dim p = BookingAdmin.ReasonProblem(reason)
            If p IsNot Nothing Then Return AdminResult.Fail("reason", p)
            Return Db.InTransaction(Of AdminResult)(
                Function(conn, tx)
                    Dim b = BookingRepository.GetForUpdate(conn, tx, bookingId)
                    If b Is Nothing Then Return AdminResult.Fail("", "This booking no longer exists.")
                    If b.Status <> BookingStatus.Unpaid Then
                        Return AdminResult.Fail("", $"This booking is {b.Status.ToLowerInvariant()}. Only unpaid bills can be voided.")
                    End If
                    BookingRepository.Close(conn, tx, b.Id, BookingStatus.Voided, reason, Session.CurrentUserId)
                    AuditLog.Write(conn, tx, AuditActions.VoidSale, "bookings", b.Id,
                                   $"Bill for {b.BookingNo} ({b.CustomerName}) voided before payment. Reason: {reason}" &
                                   If(b.Downpayment > 0D, $"; downpayment on record {Money.Format(b.Downpayment)}", ""))
                    Return AdminResult.Success(b.Id)
                End Function)
        End Function

        ''' <summary>Counts a printed copy and records the printer in the audit log. Returns the new copy count.</summary>
        Public Shared Function RecordPrint(transactionId As Integer, printerName As String) As Integer
            Session.Demand(Permissions.AddService)
            Return Db.InTransaction(Of Integer)(
                Function(conn, tx)
                    Dim count = TransactionRepository.IncrementPrinted(conn, tx, transactionId)
                    Dim sale = TransactionRepository.LockForUpdate(conn, tx, transactionId)
                    AuditLog.Write(conn, tx, AuditActions.ReceiptPrint, "transactions", transactionId,
                                   $"{If(sale?.Item3, "Receipt")} printed on '{printerName}'" & If(count > 1, $" (copy {count})", ""))
                    Return count
                End Function)
        End Function

    End Class

End Namespace
