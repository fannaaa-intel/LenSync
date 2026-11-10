Option Strict On
Option Explicit On
Option Infer On

Imports System.Data
Imports MySql.Data.MySqlClient
Imports LenSync.Core
Imports LenSync.Models

Namespace Data

    ''' <summary>
    ''' SQL for bookings. Rows are never deleted (is_active stays 1; cancelling is a status). Methods that take a
    ''' connection and transaction run inside the caller's save; the others open their own connection.
    ''' </summary>
    Public NotInheritable Class BookingRepository

        Private Sub New()
        End Sub

        Private Const SelectSql As String =
            "SELECT b.id, b.booking_no, b.customer_name, IFNULL(b.contact_no, '') AS contact_no, " &
            "IFNULL(b.customer_email, '') AS customer_email, b.package_id, IFNULL(p.name, '') AS package_name, " &
            "IFNULL(p.price, 0) AS package_price, b.schedule_at, b.downpayment, IFNULL(b.downpayment_method, '') AS downpayment_method, " &
            "IFNULL(b.downpayment_ref, '') AS downpayment_ref, b.status, IFNULL(b.notes, '') AS notes " &
            "FROM bookings b LEFT JOIN packages p ON p.id = b.package_id WHERE b.is_active = 1"

        ''' <summary>Bookings for the list. Open (to shoot / to bill) comes soonest first; history comes newest first.</summary>
        Public Shared Function List(filter As BookingFilter) As List(Of Booking)
            Dim where As String
            Dim order = "b.schedule_at DESC, b.id DESC"
            Select Case filter
                Case BookingFilter.Open
                    where = " AND b.status IN ('Pending', 'Unpaid')"
                    order = "b.schedule_at ASC, b.id ASC"
                Case BookingFilter.Paid : where = " AND b.status = 'Paid'"
                Case BookingFilter.Cancelled : where = " AND b.status = 'Cancelled'"
                Case Else : where = ""
            End Select
            Return Db.Query($"{SelectSql}{where} ORDER BY {order} LIMIT 1000").Rows.Cast(Of DataRow)().Select(AddressOf Map).ToList()
        End Function

        ''' <summary>Shoots that are done and waiting for payment, oldest first.</summary>
        Public Shared Function ListUnpaid() As List(Of Booking)
            Return Db.Query($"{SelectSql} AND b.status = 'Unpaid' ORDER BY b.schedule_at ASC, b.id ASC LIMIT 1000").
                Rows.Cast(Of DataRow)().Select(AddressOf Map).ToList()
        End Function

        Public Shared Function GetById(id As Integer) As Booking
            Dim t = Db.Query($"{SelectSql} AND b.id = @id", Db.P("@id", id))
            Return If(t.Rows.Count = 0, Nothing, Map(t.Rows(0)))
        End Function

        ''' <summary>Locks the booking row until the caller's transaction ends, then reads it.</summary>
        Public Shared Function GetForUpdate(conn As MySqlConnection, tx As MySqlTransaction, id As Integer) As Booking
            ' Lock only the bookings row (MariaDB has no FOR UPDATE OF), then read it with its package.
            Using cmd = Db.Command(conn, "SELECT id FROM bookings WHERE id = @id AND is_active = 1 FOR UPDATE", tx, Db.P("@id", id))
                If cmd.ExecuteScalar() Is Nothing Then Return Nothing
            End Using
            Using cmd = Db.Command(conn, $"{SelectSql} AND b.id = @id", tx, Db.P("@id", id))
                Using reader = cmd.ExecuteReader()
                    Dim table As New DataTable()
                    table.Load(reader)
                    Return If(table.Rows.Count = 0, Nothing, Map(table.Rows(0)))
                End Using
            End Using
        End Function

        Private Shared Function Map(r As DataRow) As Booking
            Return New Booking With {
                .Id = Db.Int(r, "id"), .BookingNo = Db.Str(r, "booking_no"), .CustomerName = Db.Str(r, "customer_name"),
                .ContactNo = Db.Str(r, "contact_no"), .CustomerEmail = Db.Str(r, "customer_email"),
                .PackageId = If(TypeOf r("package_id") Is DBNull, CType(Nothing, Integer?), Db.Int(r, "package_id")),
                .PackageName = Db.Str(r, "package_name"), .PackagePrice = Convert.ToDecimal(r("package_price")),
                .ScheduleAt = Convert.ToDateTime(r("schedule_at")), .Downpayment = Convert.ToDecimal(r("downpayment")),
                .DownpaymentMethod = Db.Str(r, "downpayment_method"), .DownpaymentRef = Db.Str(r, "downpayment_ref"),
                .Status = Db.Str(r, "status"), .Notes = Db.Str(r, "notes")}
        End Function

        Public Shared Function Insert(conn As MySqlConnection, tx As MySqlTransaction, b As Booking, bookingNo As String,
                                      status As String, createdBy As Integer) As Integer
            Const sql As String =
                "INSERT INTO bookings (booking_no, customer_name, contact_no, customer_email, package_id, schedule_at, downpayment, " &
                "downpayment_method, downpayment_ref, status, notes, created_by) VALUES (@no, @name, @contact, @email, @pkg, @at, @dp, " &
                "@dpm, @dpr, @status, @notes, @by); SELECT LAST_INSERT_ID();"
            Using cmd = Db.Command(conn, sql, tx, FieldParameters(b).Concat({
                    Db.P("@no", bookingNo), Db.P("@status", status), Db.P("@by", createdBy)}).ToArray())
                Return Db.ConvertValue(Of Integer)(cmd.ExecuteScalar())
            End Using
        End Function

        Public Shared Sub Update(conn As MySqlConnection, tx As MySqlTransaction, b As Booking)
            Const sql As String =
                "UPDATE bookings SET customer_name = @name, contact_no = @contact, customer_email = @email, package_id = @pkg, " &
                "schedule_at = @at, downpayment = @dp, downpayment_method = @dpm, downpayment_ref = @dpr, notes = @notes WHERE id = @id"
            Using cmd = Db.Command(conn, sql, tx, FieldParameters(b).Concat({Db.P("@id", b.Id)}).ToArray())
                cmd.ExecuteNonQuery()
            End Using
        End Sub

        Private Shared Function FieldParameters(b As Booking) As MySqlParameter()
            Return {
                Db.P("@name", b.CustomerName), Db.P("@contact", NullIfEmpty(b.ContactNo)), Db.P("@email", NullIfEmpty(b.CustomerEmail)),
                Db.P("@pkg", If(b.PackageId.HasValue, CObj(b.PackageId.Value), Nothing)), Db.P("@at", b.ScheduleAt),
                Db.P("@dp", b.Downpayment), Db.P("@dpm", NullIfEmpty(b.DownpaymentMethod)), Db.P("@dpr", NullIfEmpty(b.DownpaymentRef)),
                Db.P("@notes", NullIfEmpty(b.Notes))}
        End Function

        ''' <summary>Changes the status; <paramref name="note"/> (optional) is appended to the notes on a new line.</summary>
        Public Shared Sub SetStatus(conn As MySqlConnection, tx As MySqlTransaction, id As Integer, status As String, Optional note As String = Nothing)
            Const sql As String =
                "UPDATE bookings SET status = @status, notes = IF(@note IS NULL, notes, CONCAT_WS('\n', NULLIF(notes, ''), @note)) WHERE id = @id"
            Using cmd = Db.Command(conn, sql, tx, Db.P("@status", status), Db.P("@note", note), Db.P("@id", id))
                cmd.ExecuteNonQuery()
            End Using
        End Sub

        Private Shared Function NullIfEmpty(value As String) As Object
            Return If(String.IsNullOrEmpty(value), Nothing, value)
        End Function

    End Class

End Namespace
