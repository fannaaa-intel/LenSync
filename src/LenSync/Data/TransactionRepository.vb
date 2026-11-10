Option Strict On
Option Explicit On
Option Infer On

Imports System.Data
Imports MySql.Data.MySqlClient
Imports LenSync.Core
Imports LenSync.Models

Namespace Data

    ''' <summary>SQL for sales (transactions + transaction_items). Sales are voided, never deleted.</summary>
    Public NotInheritable Class TransactionRepository

        Private Sub New()
        End Sub

        Public Shared Function Insert(conn As MySqlConnection, tx As MySqlTransaction, receiptNo As String, bookingId As Integer,
                                      totals As BillTotals, discount As Discount, method As String, referenceNo As String,
                                      tendered As Decimal, change As Decimal, cashierId As Integer) As Integer
            Const sql As String =
                "INSERT INTO transactions (receipt_no, booking_id, subtotal, discount_type, discount_value, discount_amount, discount_reason, " &
                "total, downpayment, balance_due, payment_method, reference_no, amount_tendered, change_amount, status, cashier_id) " &
                "VALUES (@no, @booking, @sub, @dtype, @dvalue, @damount, @dreason, @total, @dp, @balance, @method, @ref, @tendered, " &
                "@change, 'Completed', @cashier); SELECT LAST_INSERT_ID();"
            Dim hasDiscount = discount IsNot Nothing AndAlso totals.DiscountAmount > 0D
            Using cmd = Db.Command(conn, sql, tx,
                    Db.P("@no", receiptNo), Db.P("@booking", bookingId), Db.P("@sub", totals.Subtotal),
                    Db.P("@dtype", If(hasDiscount, discount.Kind, Nothing)), Db.P("@dvalue", If(hasDiscount, discount.Value, 0D)),
                    Db.P("@damount", totals.DiscountAmount), Db.P("@dreason", If(hasDiscount, discount.Reason, Nothing)),
                    Db.P("@total", totals.Total), Db.P("@dp", totals.Downpayment), Db.P("@balance", totals.BalanceDue),
                    Db.P("@method", method), Db.P("@ref", If(String.IsNullOrEmpty(referenceNo), Nothing, referenceNo)),
                    Db.P("@tendered", tendered), Db.P("@change", change), Db.P("@cashier", cashierId))
                Return Db.ConvertValue(Of Integer)(cmd.ExecuteScalar())
            End Using
        End Function

        Public Shared Sub InsertItem(conn As MySqlConnection, tx As MySqlTransaction, transactionId As Integer, line As BillLine)
            Const sql As String =
                "INSERT INTO transaction_items (transaction_id, item_type, ref_id, description, qty, unit_price, line_total) " &
                "VALUES (@tx, @type, @ref, @desc, @qty, @price, @total)"
            Using cmd = Db.Command(conn, sql, tx,
                    Db.P("@tx", transactionId), Db.P("@type", line.ItemType),
                    Db.P("@ref", If(line.RefId.HasValue, CObj(line.RefId.Value), Nothing)), Db.P("@desc", line.Description),
                    Db.P("@qty", line.Qty), Db.P("@price", line.UnitPrice), Db.P("@total", line.LineTotal))
                cmd.ExecuteNonQuery()
            End Using
        End Sub

        ''' <summary>Sales newest first. <paramref name="fromDate"/> Nothing = all dates.</summary>
        Public Shared Function List(fromDate As DateTime?) As List(Of SaleSummary)
            Dim sql =
                "SELECT t.id, t.receipt_no, t.created_at, b.customer_name, b.booking_no, t.payment_method, t.total, t.balance_due, " &
                "u.full_name AS cashier, t.status FROM transactions t " &
                "JOIN bookings b ON b.id = t.booking_id JOIN users u ON u.id = t.cashier_id" &
                If(fromDate.HasValue, " WHERE t.created_at >= @from", "") &
                " ORDER BY t.created_at DESC, t.id DESC LIMIT 1000"
            Dim rows = If(fromDate.HasValue, Db.Query(sql, Db.P("@from", fromDate.Value)), Db.Query(sql))
            Return rows.Rows.Cast(Of DataRow)().Select(
                Function(r) New SaleSummary With {
                    .Id = Db.Int(r, "id"), .ReceiptNo = Db.Str(r, "receipt_no"), .CreatedAt = Convert.ToDateTime(r("created_at")),
                    .CustomerName = Db.Str(r, "customer_name"), .BookingNo = Db.Str(r, "booking_no"),
                    .PaymentMethod = Db.Str(r, "payment_method"), .Total = Convert.ToDecimal(r("total")),
                    .BalanceDue = Convert.ToDecimal(r("balance_due")), .CashierName = Db.Str(r, "cashier"), .Status = Db.Str(r, "status")}).ToList()
        End Function

        ''' <summary>The receipt's sale data (studio details are added by the caller). Nothing if it doesn't exist.</summary>
        Public Shared Function GetReceipt(id As Integer) As Receipt
            Const sql As String =
                "SELECT t.*, b.booking_no, b.customer_name, IFNULL(b.contact_no, '') AS contact_no, b.schedule_at, " &
                "IFNULL(b.downpayment_method, '') AS dp_method, IFNULL(b.downpayment_ref, '') AS dp_ref, u.full_name AS cashier " &
                "FROM transactions t JOIN bookings b ON b.id = t.booking_id JOIN users u ON u.id = t.cashier_id WHERE t.id = @id"
            Using conn = Db.Open()
                Dim head As DataRow
                Using cmd = Db.Command(conn, sql, Nothing, Db.P("@id", id))
                    Using reader = cmd.ExecuteReader()
                        Dim t As New DataTable()
                        t.Load(reader)
                        If t.Rows.Count = 0 Then Return Nothing
                        head = t.Rows(0)
                    End Using
                End Using
                Dim r As New Receipt With {
                    .TransactionId = id, .ReceiptNo = Db.Str(head, "receipt_no"), .CreatedAt = Convert.ToDateTime(head("created_at")),
                    .BookingNo = Db.Str(head, "booking_no"), .CustomerName = Db.Str(head, "customer_name"), .ContactNo = Db.Str(head, "contact_no"),
                    .ScheduleAt = Convert.ToDateTime(head("schedule_at")),
                    .Subtotal = Convert.ToDecimal(head("subtotal")), .DiscountType = If(Db.Str(head, "discount_type"), ""),
                    .DiscountValue = Convert.ToDecimal(head("discount_value")), .DiscountAmount = Convert.ToDecimal(head("discount_amount")),
                    .DiscountReason = If(Db.Str(head, "discount_reason"), ""), .Total = Convert.ToDecimal(head("total")),
                    .Downpayment = Convert.ToDecimal(head("downpayment")), .DownpaymentMethod = Db.Str(head, "dp_method"),
                    .DownpaymentRef = Db.Str(head, "dp_ref"), .BalanceDue = Convert.ToDecimal(head("balance_due")),
                    .PaymentMethod = Db.Str(head, "payment_method"), .ReferenceNo = If(Db.Str(head, "reference_no"), ""),
                    .AmountTendered = Convert.ToDecimal(head("amount_tendered")), .ChangeAmount = Convert.ToDecimal(head("change_amount")),
                    .CashierName = Db.Str(head, "cashier"), .Status = Db.Str(head, "status"), .VoidReason = If(Db.Str(head, "void_reason"), ""),
                    .VoidedAt = Db.NullableDate(head, "voided_at"), .PrintedCount = Db.Int(head, "printed_count")}
                Using cmd = Db.Command(conn, "SELECT item_type, ref_id, description, qty, unit_price FROM transaction_items WHERE transaction_id = @id ORDER BY id",
                                       Nothing, Db.P("@id", id))
                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            r.Lines.Add(New BillLine With {
                                .ItemType = reader.GetString(0),
                                .RefId = If(reader.IsDBNull(1), CType(Nothing, Integer?), reader.GetInt32(1)),
                                .Description = reader.GetString(2), .Qty = reader.GetInt32(3), .UnitPrice = reader.GetDecimal(4)})
                        End While
                    End Using
                End Using
                Return r
            End Using
        End Function

        ''' <summary>Locks the sale row; returns (status, booking id, receipt no, total) or Nothing.</summary>
        Public Shared Function LockForUpdate(conn As MySqlConnection, tx As MySqlTransaction, id As Integer) As Tuple(Of String, Integer, String, Decimal)
            Using cmd = Db.Command(conn, "SELECT status, booking_id, receipt_no, total FROM transactions WHERE id = @id FOR UPDATE", tx, Db.P("@id", id))
                Using reader = cmd.ExecuteReader()
                    If Not reader.Read() Then Return Nothing
                    Return Tuple.Create(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetDecimal(3))
                End Using
            End Using
        End Function

        Public Shared Sub SetVoided(conn As MySqlConnection, tx As MySqlTransaction, id As Integer, reason As String, userId As Integer)
            Using cmd = Db.Command(conn, "UPDATE transactions SET status = 'Voided', void_reason = @reason, voided_by = @by, voided_at = NOW() WHERE id = @id",
                                   tx, Db.P("@reason", reason), Db.P("@by", userId), Db.P("@id", id))
                cmd.ExecuteNonQuery()
            End Using
        End Sub

        ''' <summary>Adds one to printed_count and returns the new count.</summary>
        Public Shared Function IncrementPrinted(conn As MySqlConnection, tx As MySqlTransaction, id As Integer) As Integer
            Using cmd = Db.Command(conn, "UPDATE transactions SET printed_count = printed_count + 1 WHERE id = @id; " &
                                         "SELECT printed_count FROM transactions WHERE id = @id", tx, Db.P("@id", id))
                Return Db.ConvertValue(Of Integer)(cmd.ExecuteScalar())
            End Using
        End Function

    End Class

End Namespace
