Option Strict On
Option Explicit On
Option Infer On

Imports System.Data
Imports MySql.Data.MySqlClient
Imports LenSync.Core
Imports LenSync.Models

Namespace Data

    ''' <summary>
    ''' SQL for the reports and report_logs. Every period is [from 00:00, day after "to" 00:00). "Sales" are Completed
    ''' receipts by the date they were issued; voided receipts never count toward amounts.
    ''' </summary>
    Public NotInheritable Class ReportRepository

        Private Sub New()
        End Sub

        Private Shared Function Range(fromDay As Date, toDay As Date) As MySqlParameter()
            Return {Db.P("@from", fromDay.Date), Db.P("@to", toDay.Date.AddDays(1))}
        End Function

        Private Const InPeriod As String = "t.created_at >= @from AND t.created_at < @to"

        ''' <summary>One row: receipts, gross, discounts, net, voided count for the period.</summary>
        Public Shared Function Totals(fromDay As Date, toDay As Date) As DataRow
            Return Db.Query(
                "SELECT SUM(t.status = 'Completed') AS receipts, " &
                "IFNULL(SUM(CASE WHEN t.status = 'Completed' THEN t.subtotal END), 0) AS gross, " &
                "IFNULL(SUM(CASE WHEN t.status = 'Completed' THEN t.discount_amount END), 0) AS discounts, " &
                "IFNULL(SUM(CASE WHEN t.status = 'Completed' THEN t.total END), 0) AS net, " &
                "SUM(t.status = 'Voided') AS voided FROM transactions t WHERE " & InPeriod, Range(fromDay, toDay)).Rows(0)
        End Function

        Public Shared Function DailySales(fromDay As Date, toDay As Date) As DataTable
            Return Db.Query(
                "SELECT DATE(t.created_at) AS d, COUNT(*) AS receipts, SUM(t.subtotal) AS gross, SUM(t.discount_amount) AS discounts, " &
                "SUM(t.total) AS net, SUM(t.downpayment) AS downpayments, SUM(t.balance_due) AS balances " &
                "FROM transactions t WHERE t.status = 'Completed' AND " & InPeriod & " GROUP BY DATE(t.created_at) ORDER BY d", Range(fromDay, toDay))
        End Function

        Public Shared Function Transactions(fromDay As Date, toDay As Date) As DataTable
            Return Db.Query(
                "SELECT t.receipt_no, t.created_at, b.customer_name, u.full_name AS cashier, t.payment_method, t.total, t.status " &
                "FROM transactions t JOIN bookings b ON b.id = t.booking_id JOIN users u ON u.id = t.cashier_id " &
                "WHERE " & InPeriod & " ORDER BY t.created_at, t.id", Range(fromDay, toDay))
        End Function

        ''' <summary>Balance payments by the sale's payment method.</summary>
        Public Shared Function BalancesByMethod(fromDay As Date, toDay As Date) As DataTable
            Return Db.Query(
                "SELECT t.payment_method AS method, SUM(t.balance_due > 0) AS payments, SUM(t.balance_due) AS amount " &
                "FROM transactions t WHERE t.status = 'Completed' AND " & InPeriod & " GROUP BY t.payment_method", Range(fromDay, toDay))
        End Function

        ''' <summary>Downpayments applied on the period's sales, by how the downpayment was paid.</summary>
        Public Shared Function DownpaymentsByMethod(fromDay As Date, toDay As Date) As DataTable
            Return Db.Query(
                "SELECT IFNULL(b.downpayment_method, 'Cash') AS method, COUNT(*) AS payments, SUM(t.downpayment) AS amount " &
                "FROM transactions t JOIN bookings b ON b.id = t.booking_id " &
                "WHERE t.status = 'Completed' AND t.downpayment > 0 AND " & InPeriod & " GROUP BY IFNULL(b.downpayment_method, 'Cash')",
                Range(fromDay, toDay))
        End Function

        Public Shared Function ByStaff(fromDay As Date, toDay As Date) As DataTable
            Return Db.Query(
                "SELECT u.full_name AS cashier, u.username, SUM(t.status = 'Completed') AS receipts, " &
                "IFNULL(SUM(CASE WHEN t.status = 'Completed' THEN t.total END), 0) AS net, " &
                "IFNULL(SUM(CASE WHEN t.status = 'Completed' THEN t.discount_amount END), 0) AS discounts, " &
                "SUM(t.status = 'Voided') AS voided " &
                "FROM transactions t JOIN users u ON u.id = t.cashier_id WHERE " & InPeriod &
                " GROUP BY u.id, u.full_name, u.username ORDER BY net DESC, u.full_name", Range(fromDay, toDay))
        End Function

        ''' <summary>Billed lines of one item type (Package / Addon / Other), grouped by the name at the time of sale.</summary>
        Public Shared Function ItemsByName(fromDay As Date, toDay As Date, itemType As String) As DataTable
            Return Db.Query(
                "SELECT i.description AS name, SUM(i.qty) AS qty, COUNT(DISTINCT i.transaction_id) AS receipts, SUM(i.line_total) AS amount " &
                "FROM transaction_items i JOIN transactions t ON t.id = i.transaction_id " &
                "WHERE t.status = 'Completed' AND i.item_type = @type AND " & InPeriod &
                " GROUP BY i.description ORDER BY amount DESC, i.description",
                Range(fromDay, toDay).Concat({Db.P("@type", itemType)}).ToArray())
        End Function

        ' ================================================================ report_logs
        Public Shared Function InsertLog(conn As MySqlConnection, tx As MySqlTransaction, data As ReportData, filtersJson As String, userId As Integer) As Integer
            Const sql As String =
                "INSERT INTO report_logs (report_no, report_type, date_from, date_to, filters_json, row_count, total_amount, content_hash, " &
                "verification_code, generated_by, printer_name, generated_at) VALUES (@no, @type, @from, @to, @filters, @rows, @total, @hash, " &
                "@code, @by, @printer, @at); SELECT LAST_INSERT_ID();"
            Using cmd = Db.Command(conn, sql, tx,
                    Db.P("@no", data.ReportNo), Db.P("@type", ReportTypes.Key(data.Type)), Db.P("@from", data.DateFrom.Date),
                    Db.P("@to", data.DateTo.Date), Db.P("@filters", filtersJson), Db.P("@rows", data.RowCount), Db.P("@total", data.NetSales),
                    Db.P("@hash", data.ContentHash), Db.P("@code", data.VerificationCode), Db.P("@by", userId),
                    Db.P("@printer", data.PrinterName), Db.P("@at", data.GeneratedAt.Value))
                Return Db.ConvertValue(Of Integer)(cmd.ExecuteScalar())
            End Using
        End Function

        Private Const LogSelect As String =
            "SELECT r.id, r.report_no, r.report_type, r.date_from, r.date_to, r.row_count, r.total_amount, r.content_hash, " &
            "r.verification_code, u.full_name AS generated_by, IFNULL(r.printer_name, '') AS printer_name, r.generated_at " &
            "FROM report_logs r JOIN users u ON u.id = r.generated_by"

        Public Shared Function ListLogs() As List(Of ReportLog)
            Return Db.Query(LogSelect & " ORDER BY r.generated_at DESC, r.id DESC LIMIT 1000").Rows.Cast(Of DataRow)().Select(AddressOf MapLog).ToList()
        End Function

        Public Shared Function FindLog(reportNo As String) As ReportLog
            Dim t = Db.Query(LogSelect & " WHERE r.report_no = @no", Db.P("@no", reportNo))
            Return If(t.Rows.Count = 0, Nothing, MapLog(t.Rows(0)))
        End Function

        Private Shared Function MapLog(r As DataRow) As ReportLog
            Return New ReportLog With {
                .Id = Db.Int(r, "id"), .ReportNo = Db.Str(r, "report_no"), .TypeKey = Db.Str(r, "report_type"),
                .DateFrom = Convert.ToDateTime(r("date_from")), .DateTo = Convert.ToDateTime(r("date_to")),
                .RowCount = Db.Int(r, "row_count"), .TotalAmount = Convert.ToDecimal(r("total_amount")),
                .ContentHash = Db.Str(r, "content_hash"), .VerificationCode = Db.Str(r, "verification_code"),
                .GeneratedBy = Db.Str(r, "generated_by"), .PrinterName = Db.Str(r, "printer_name"),
                .GeneratedAt = Convert.ToDateTime(r("generated_at"))}
        End Function

    End Class

End Namespace
