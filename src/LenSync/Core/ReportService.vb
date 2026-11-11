Option Strict On
Option Explicit On
Option Infer On

Imports System.Data
Imports System.Globalization
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.RegularExpressions
Imports LenSync.Data
Imports LenSync.Models

Namespace Core

    ''' <summary>
    ''' Reports: build the five report types, issue a printed report (number + proof in one DB transaction) and verify
    ''' a paper report. Proof: content_hash = SHA-256 of the report's data (canonical text); verification_code = first 10
    ''' Base32 characters of HMAC-SHA256(content_hash + report_no) keyed with settings.report_secret.
    ''' </summary>
    Public NotInheritable Class ReportService

        Private Sub New()
        End Sub

        Public Const MaxDays As Integer = 366
        Private Shared ReadOnly PH As CultureInfo = CultureInfo.GetCultureInfo("en-PH")
        Private Shared ReadOnly ReportNoPattern As New Regex("^RPT-\d{4}-\d{4,}$")
        Private Shared ReadOnly CodePattern As New Regex("^[A-Z2-7]{10}$")

        ' ================================================================ validation
        Public Shared Function RangeProblem(fromDay As Date, toDay As Date) As String
            If toDay.Date < fromDay.Date Then Return "The end date is before the start date."
            If (toDay.Date - fromDay.Date).TotalDays >= MaxDays Then Return "Choose a period of at most one year."
            Return Nothing
        End Function

        Public Shared Function PeriodText(fromDay As Date, toDay As Date) As String
            If fromDay.Date = toDay.Date Then Return fromDay.ToString("dddd, MMMM d, yyyy", PH)
            If fromDay.Year = toDay.Year Then Return $"{fromDay.ToString("MMM d", PH)} – {toDay.ToString("MMM d, yyyy", PH)}"
            Return $"{fromDay.ToString("MMM d, yyyy", PH)} – {toDay.ToString("MMM d, yyyy", PH)}"
        End Function

        ' ================================================================ build
        Public Shared Function Build(type As ReportType, fromDay As Date, toDay As Date) As ReportData
            Session.Demand(Permissions.Reports)
            Dim p = RangeProblem(fromDay, toDay)
            If p IsNot Nothing Then Throw New ArgumentException(p)
            Dim d = BuildCore(type, fromDay.Date, toDay.Date)
            d.BusinessName = AppSettings.GetValue(AppSettings.BusinessName, "LENSYNC Photo Studio")
            d.Address = AppSettings.GetValue(AppSettings.Address)
            d.Contact = AppSettings.GetValue(AppSettings.Contact)
            d.LogoPath = AppSettings.GetValue(AppSettings.LogoPath)
            d.SignaturePath = AppSettings.GetValue(AppSettings.SignaturePath)
            Return d
        End Function

        ''' <summary>The report's data only (no studio details, no proof): exactly what the fingerprint covers.</summary>
        Private Shared Function BuildCore(type As ReportType, fromDay As Date, toDay As Date) As ReportData
            Dim d As New ReportData With {.Type = type, .DateFrom = fromDay, .DateTo = toDay}
            Dim tot = ReportRepository.Totals(fromDay, toDay)
            Dim receipts = Db.Int(tot, "receipts")
            Dim discounts = Convert.ToDecimal(tot("discounts"))
            d.NetSales = Convert.ToDecimal(tot("net"))
            d.Summary.Add(Pair("Net sales", Money.Format(d.NetSales)))
            d.Summary.Add(Pair("Receipts", receipts.ToString("#,##0", CultureInfo.InvariantCulture)))
            d.Summary.Add(Pair("Discounts given", Money.Format(discounts)))
            d.Summary.Add(Pair("Voided receipts", Db.Int(tot, "voided").ToString("#,##0", CultureInfo.InvariantCulture)))
            Select Case type
                Case ReportType.DailySales : d.Tables.Add(DailyTable(fromDay, toDay))
                Case ReportType.Transactions : d.Tables.Add(TransactionsTable(fromDay, toDay))
                Case ReportType.PaymentMethods : d.Tables.Add(MethodsTable(fromDay, toDay))
                Case ReportType.Staff : d.Tables.Add(StaffTable(fromDay, toDay))
                Case Else : d.Tables.AddRange(PackageTables(fromDay, toDay))
            End Select
            Return d
        End Function

        Private Shared Function Pair(k As String, v As String) As KeyValuePair(Of String, String)
            Return New KeyValuePair(Of String, String)(k, v)
        End Function

        Private Shared Function M(value As Decimal) As String
            Return Money.FormatPlain(value)
        End Function

        Private Shared Function N(value As Integer) As String
            Return value.ToString("#,##0", CultureInfo.InvariantCulture)
        End Function

        Private Shared Function Dec(r As DataRow, column As String) As Decimal
            Dim v = r(column)
            Return If(TypeOf v Is DBNull, 0D, Convert.ToDecimal(v, CultureInfo.InvariantCulture))
        End Function

        Private Shared Function DailyTable(fromDay As Date, toDay As Date) As ReportTable
            Dim t As New ReportTable()
            t.AddColumn("Date", 20)
            t.AddColumn("Receipts", 10, True)
            t.AddColumn("Gross (₱)", 15, True)
            t.AddColumn("Discounts (₱)", 14, True)
            t.AddColumn("Net sales (₱)", 15, True)
            t.AddColumn("Downpayments (₱)", 15, True)
            t.AddColumn("Balances paid (₱)", 15, True)
            Dim byDay = ReportRepository.DailySales(fromDay, toDay).Rows.Cast(Of DataRow)().ToDictionary(Function(r) Convert.ToDateTime(r("d")).Date)
            Dim sums(5) As Decimal
            Dim day = fromDay
            While day <= toDay
                Dim r As DataRow = Nothing
                Dim values = {0D, 0D, 0D, 0D, 0D, 0D}
                If byDay.TryGetValue(day, r) Then
                    values = {Dec(r, "receipts"), Dec(r, "gross"), Dec(r, "discounts"), Dec(r, "net"), Dec(r, "downpayments"), Dec(r, "balances")}
                End If
                For i = 0 To 5
                    sums(i) += values(i)
                Next
                t.AddRow(False, day.ToString("ddd, MMM d, yyyy", PH), N(CInt(values(0))), M(values(1)), M(values(2)), M(values(3)), M(values(4)), M(values(5)))
                day = day.AddDays(1)
            End While
            t.Totals = {"Total", N(CInt(sums(0))), M(sums(1)), M(sums(2)), M(sums(3)), M(sums(4)), M(sums(5))}
            t.Note = "Net sales = gross − discounts. Downpayments are counted on the day of the receipt they were applied to."
            Return t
        End Function

        Private Shared Function TransactionsTable(fromDay As Date, toDay As Date) As ReportTable
            Dim t As New ReportTable()
            ' Widths fit "Nov 10, 2026 12:24 PM" and a full-name cashier on letter paper at the print body size.
            t.AddColumn("Receipt no.", 14)
            t.AddColumn("Date & time", 20)
            t.AddColumn("Customer", 17)
            t.AddColumn("Cashier", 19)
            t.AddColumn("Paid via", 9)
            t.AddColumn("Total (₱)", 11, True)
            t.AddColumn("Status", 10)
            Dim net = 0D
            For Each r As DataRow In ReportRepository.Transactions(fromDay, toDay).Rows
                Dim voided = Db.Str(r, "status") = "Voided"
                Dim total = Dec(r, "total")
                If Not voided Then net += total
                t.AddRow(voided, Db.Str(r, "receipt_no"), Convert.ToDateTime(r("created_at")).ToString("MMM d, yyyy h:mm tt", PH),
                         Db.Str(r, "customer_name"), Db.Str(r, "cashier"), Db.Str(r, "payment_method"), M(total), Db.Str(r, "status"))
            Next
            t.Totals = {"Total", "", "", "", "", M(net), ""}
            t.Note = "Voided receipts are listed (status Voided, printed lighter) but are not included in the total."
            Return t
        End Function

        Private Shared Function MethodsTable(fromDay As Date, toDay As Date) As ReportTable
            Dim t As New ReportTable()
            t.AddColumn("Payment method", 28)
            t.AddColumn("Downpayments (₱)", 24, True)
            t.AddColumn("Balance payments (₱)", 24, True)
            t.AddColumn("Total received (₱)", 24, True)
            Dim dps = ReportRepository.DownpaymentsByMethod(fromDay, toDay).Rows.Cast(Of DataRow)().ToDictionary(Function(r) Db.Str(r, "method"), Function(r) Dec(r, "amount"))
            Dim bals = ReportRepository.BalancesByMethod(fromDay, toDay).Rows.Cast(Of DataRow)().ToDictionary(Function(r) Db.Str(r, "method"), Function(r) Dec(r, "amount"))
            Dim sumDp = 0D, sumBal = 0D
            For Each method In PaymentMethods.All
                Dim dp = 0D, bal = 0D
                dps.TryGetValue(method, dp)
                bals.TryGetValue(method, bal)
                sumDp += dp
                sumBal += bal
                t.AddRow(False, method, M(dp), M(bal), M(dp + bal))
            Next
            t.Totals = {"Total", M(sumDp), M(sumBal), M(sumDp + sumBal)}
            t.Note = "Total received equals net sales: each receipt's downpayment plus the balance paid when it was billed."
            Return t
        End Function

        Private Shared Function StaffTable(fromDay As Date, toDay As Date) As ReportTable
            Dim t As New ReportTable()
            t.AddColumn("Cashier", 34)
            t.AddColumn("Receipts", 13, True)
            t.AddColumn("Net sales (₱)", 20, True)
            t.AddColumn("Discounts (₱)", 19, True)
            t.AddColumn("Voided", 14, True)
            Dim receipts = 0, voided = 0
            Dim net = 0D, disc = 0D
            For Each r As DataRow In ReportRepository.ByStaff(fromDay, toDay).Rows
                Dim rc = CInt(Dec(r, "receipts")), vc = CInt(Dec(r, "voided"))
                Dim nv = Dec(r, "net"), dv = Dec(r, "discounts")
                receipts += rc : voided += vc : net += nv : disc += dv
                t.AddRow(False, $"{Db.Str(r, "cashier")} ({Db.Str(r, "username")})", N(rc), M(nv), M(dv), N(vc))
            Next
            t.Totals = {"Total", N(receipts), M(net), M(disc), N(voided)}
            Return t
        End Function

        Private Shared Function PackageTables(fromDay As Date, toDay As Date) As List(Of ReportTable)
            Dim result As New List(Of ReportTable)
            For Each spec In {Tuple.Create(ItemTypes.Package, "Packages", "Package", "Times billed"),
                              Tuple.Create(ItemTypes.Addon, "Add-ons", "Add-on", "Quantity"),
                              Tuple.Create(ItemTypes.Other, "Other items", "Item", "Quantity")}
                Dim rows = ReportRepository.ItemsByName(fromDay, toDay, spec.Item1)
                If spec.Item1 = ItemTypes.Other AndAlso rows.Rows.Count = 0 Then Continue For
                Dim t As New ReportTable With {.Title = spec.Item2}
                t.AddColumn(spec.Item3, 50)
                t.AddColumn(spec.Item4, 16, True)
                t.AddColumn("Receipts", 14, True)
                t.AddColumn("Amount (₱)", 20, True)
                Dim qty = 0, rc = 0
                Dim amount = 0D
                For Each r As DataRow In rows.Rows
                    Dim q = CInt(Dec(r, "qty")), c = CInt(Dec(r, "receipts"))
                    Dim a = Dec(r, "amount")
                    qty += q : rc += c : amount += a
                    t.AddRow(False, Db.Str(r, "name"), N(q), N(c), M(a))
                Next
                t.Totals = {"Total", N(qty), "", M(amount)}
                result.Add(t)
            Next
            result(result.Count - 1).Note = "Amounts are before discounts (see Discounts given in the summary)."
            Return result
        End Function

        ' ================================================================ proof
        ''' <summary>SHA-256 (lowercase hex) of the report's data in a fixed text form.</summary>
        Public Shared Function ComputeHash(d As ReportData) As String
            Dim sb As New StringBuilder()
            sb.Append("LENSYNC-REPORT/1").Append(vbLf)
            sb.Append(ReportTypes.Key(d.Type)).Append(vbLf)
            sb.Append(d.DateFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(vbLf)
            sb.Append(d.DateTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(vbLf)
            For Each kv In d.Summary
                sb.Append(kv.Key).Append("=").Append(kv.Value).Append(vbLf)
            Next
            For Each t In d.Tables
                sb.Append("#").Append(t.Title).Append(vbLf)
                sb.Append(String.Join("|", t.Columns.Select(Function(c) c.Header))).Append(vbLf)
                For i = 0 To t.Rows.Count - 1
                    sb.Append(String.Join("|", t.Rows(i))).Append(If(t.Muted(i), "|~", "")).Append(vbLf)
                Next
                If t.Totals IsNot Nothing Then sb.Append("T|").Append(String.Join("|", t.Totals)).Append(vbLf)
            Next
            Using sha = SHA256.Create()
                Dim bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))
                Return String.Concat(bytes.Select(Function(b) b.ToString("x2", CultureInfo.InvariantCulture)))
            End Using
        End Function

        ''' <summary>First 10 Base32 characters of HMAC-SHA256(contentHash + reportNo) with the studio's report secret.</summary>
        Public Shared Function ComputeCode(contentHash As String, reportNo As String) As String
            Dim secret = AppSettings.GetValue(AppSettings.ReportSecret)
            If String.IsNullOrEmpty(secret) Then
                AppSettings.EnsureReportSecret()
                secret = AppSettings.GetValue(AppSettings.ReportSecret)
            End If
            Using h As New HMACSHA256(Encoding.UTF8.GetBytes(secret))
                Return Base32(h.ComputeHash(Encoding.UTF8.GetBytes(contentHash & reportNo))).Substring(0, 10)
            End Using
        End Function

        Private Shared Function Base32(data As Byte()) As String
            Const alphabet As String = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
            Dim sb As New StringBuilder()
            Dim buffer = 0, bits = 0
            For Each b In data
                buffer = (buffer << 8) Or b
                bits += 8
                While bits >= 5
                    sb.Append(alphabet((buffer >> (bits - 5)) And 31))
                    bits -= 5
                End While
                buffer = buffer And ((1 << bits) - 1)
            Next
            If bits > 0 Then sb.Append(alphabet((buffer << (5 - bits)) And 31))
            Return sb.ToString()
        End Function

        ''' <summary>"ABCDEFGHIJ" -> "ABCDE-FGHIJ" (easier to read and type).</summary>
        Public Shared Function FormatCode(code As String) As String
            If String.IsNullOrEmpty(code) OrElse code.Length <> 10 Then Return If(code, "")
            Return code.Substring(0, 5) & "-" & code.Substring(5)
        End Function

        Public Shared Function NormalizeReportNo(text As String) As String
            Return If(text, "").Trim().ToUpperInvariant().Replace(" ", "")
        End Function

        Public Shared Function NormalizeCode(text As String) As String
            Return If(text, "").ToUpperInvariant().Replace("-", "").Replace(" ", "").Trim()
        End Function

        Public Shared Function ReportNoProblem(text As String) As String
            If NormalizeReportNo(text).Length = 0 Then Return "Enter the report number, e.g. RPT-2026-0001."
            If Not ReportNoPattern.IsMatch(NormalizeReportNo(text)) Then Return "Use the format RPT-2026-0001."
            Return Nothing
        End Function

        Public Shared Function CodeProblem(text As String) As String
            If NormalizeCode(text).Length = 0 Then Return "Enter the 10-character verification code."
            If Not CodePattern.IsMatch(NormalizeCode(text)) Then Return "The code has 10 letters and digits (2-7), e.g. ABCDE-FGH23."
            Return Nothing
        End Function

        Private Shared Function SameText(a As String, b As String) As Boolean
            ' Constant-time compare so timing doesn't hint how much of a guessed code is right.
            If a Is Nothing OrElse b Is Nothing OrElse a.Length <> b.Length Then Return False
            Dim diff = 0
            For i = 0 To a.Length - 1
                diff = diff Or (AscW(a(i)) Xor AscW(b(i)))
            Next
            Return diff = 0
        End Function

        ' ================================================================ issue / history / verify
        ''' <summary>
        ''' Gives the report its number and proof, records it in report_logs with the printer, and audits it, all in one
        ''' DB transaction. Call right before printing; on failure the report stays unissued.
        ''' </summary>
        Public Shared Sub Issue(d As ReportData, printerName As String)
            Session.Demand(Permissions.Reports)
            Dim at = DateTime.Now
            at = New DateTime(at.Year, at.Month, at.Day, at.Hour, at.Minute, at.Second)
            Dim hash = ComputeHash(d)
            Try
                Db.InTransaction(
                    Sub(conn, tx)
                        d.ReportNo = Db.NextDocumentNo(conn, tx, "RPT", 4)
                        d.ContentHash = hash
                        d.VerificationCode = ComputeCode(hash, d.ReportNo)
                        d.GeneratedBy = Session.CurrentUser.FullName
                        d.GeneratedAt = at
                        d.PrinterName = If(printerName, "")
                        Dim logId = ReportRepository.InsertLog(conn, tx, d, $"{{""type"":""{ReportTypes.Key(d.Type)}""}}", Session.CurrentUserId)
                        AuditLog.Write(conn, tx, AuditActions.ReportPrint, "report_logs", logId,
                                       $"{d.ReportNo}: {d.Title}, {PeriodText(d.DateFrom, d.DateTo)}, net {Money.Format(d.NetSales)}, printed on '{d.PrinterName}'")
                    End Sub)
            Catch
                d.ReportNo = ""
                d.ContentHash = ""
                d.VerificationCode = ""
                d.GeneratedAt = Nothing
                Throw
            End Try
        End Sub

        Public Shared Function History() As List(Of ReportLog)
            Session.Demand(Permissions.Reports)
            Return ReportRepository.ListLogs()
        End Function

        ''' <summary>Checks a paper report's number and code; for a genuine one, also whether the period's data changed since.</summary>
        Public Shared Function Verify(reportNo As String, code As String) As VerifyResult
            Session.Demand(Permissions.Reports)
            Dim no = NormalizeReportNo(reportNo)
            Dim c = NormalizeCode(code)
            Dim log = ReportRepository.FindLog(no)
            If log Is Nothing Then
                AuditLog.Write(AuditActions.ReportVerify, "report_logs", Nothing, $"{no}: not found")
                Return New VerifyResult With {.Outcome = VerifyOutcome.NotFound}
            End If
            Dim expected = ComputeCode(log.ContentHash, log.ReportNo)
            If Not (SameText(expected, c) AndAlso SameText(log.VerificationCode, c)) Then
                AuditLog.Write(AuditActions.ReportVerify, "report_logs", log.Id, $"{no}: code does not match")
                Return New VerifyResult With {.Outcome = VerifyOutcome.CodeMismatch}
            End If
            Dim unchanged = False
            Dim t As ReportType
            If ReportTypes.FromKey(log.TypeKey, t) Then unchanged = ComputeHash(BuildCore(t, log.DateFrom.Date, log.DateTo.Date)) = log.ContentHash
            AuditLog.Write(AuditActions.ReportVerify, "report_logs", log.Id, $"{no}: genuine; data {If(unchanged, "unchanged", "changed since printing")}")
            Return New VerifyResult With {.Outcome = VerifyOutcome.Genuine, .Log = log, .DataUnchanged = unchanged}
        End Function

    End Class

End Namespace
