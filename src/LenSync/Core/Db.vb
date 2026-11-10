Option Strict On
Option Explicit On
Option Infer On

Imports System.Configuration
Imports System.Data
Imports MySql.Data.MySqlClient

Namespace Core

    ''' <summary>
    ''' Database access helpers. The connection string comes ONLY from App.config ("LenSyncDb").
    ''' Every query is parameterized: pass values with Db.P("@name", value), never concatenate input into SQL.
    ''' </summary>
    Public NotInheritable Class Db

        Private Const ConnectionName As String = "LenSyncDb"
        ' DATETIME defaults (CURRENT_TIMESTAMP) follow the session time zone; pin it to Asia/Manila.
        Private Const SessionSetup As String = "SET time_zone = '+08:00'"

        Private Shared _connectionString As String

        Private Sub New()
        End Sub

        Public Shared ReadOnly Property ConnectionString As String
            Get
                If _connectionString Is Nothing Then
                    Dim entry = ConfigurationManager.ConnectionStrings(ConnectionName)
                    If entry Is Nothing OrElse String.IsNullOrWhiteSpace(entry.ConnectionString) Then
                        Throw New ConfigurationErrorsException($"Connection string '{ConnectionName}' is missing from App.config.")
                    End If
                    _connectionString = entry.ConnectionString
                End If
                Return _connectionString
            End Get
        End Property

        ''' <summary>Opens a new pooled connection. Always wrap in a Using block.</summary>
        Public Shared Function Open() As MySqlConnection
            Dim conn As New MySqlConnection(ConnectionString)
            Try
                conn.Open()
                Using cmd As New MySqlCommand(SessionSetup, conn)
                    cmd.ExecuteNonQuery()
                End Using
                Return conn
            Catch
                conn.Dispose()
                Throw
            End Try
        End Function

        ''' <summary>A query parameter. Nothing becomes SQL NULL.</summary>
        Public Shared Function P(name As String, value As Object) As MySqlParameter
            Return New MySqlParameter(name, If(value, DBNull.Value))
        End Function

        Public Shared Function Command(conn As MySqlConnection, sql As String, tx As MySqlTransaction,
                                       ParamArray parameters As MySqlParameter()) As MySqlCommand
            Dim cmd As New MySqlCommand(sql, conn, tx)
            If parameters IsNot Nothing Then
                For Each prm In parameters
                    cmd.Parameters.AddWithValue(prm.ParameterName, prm.Value)
                Next
            End If
            Return cmd
        End Function

        ' ---------------------------------------------------------------- one-shot helpers (own connection)
        Public Shared Function Execute(sql As String, ParamArray parameters As MySqlParameter()) As Integer
            Using conn = Open(), cmd = Command(conn, sql, Nothing, parameters)
                Return cmd.ExecuteNonQuery()
            End Using
        End Function

        Public Shared Function Scalar(Of T)(sql As String, ParamArray parameters As MySqlParameter()) As T
            Using conn = Open(), cmd = Command(conn, sql, Nothing, parameters)
                Return ConvertValue(Of T)(cmd.ExecuteScalar())
            End Using
        End Function

        Public Shared Function Query(sql As String, ParamArray parameters As MySqlParameter()) As DataTable
            Using conn = Open(), cmd = Command(conn, sql, Nothing, parameters)
                Using reader = cmd.ExecuteReader()
                    Dim table As New DataTable()
                    table.Load(reader)
                    Return table
                End Using
            End Using
        End Function

        ''' <summary>Converts a scalar result; NULL/DBNull gives the type's default.</summary>
        Public Shared Function ConvertValue(Of T)(value As Object) As T
            If value Is Nothing OrElse TypeOf value Is DBNull Then Return Nothing
            If TypeOf value Is T Then Return DirectCast(value, T)
            Dim target = If(Nullable.GetUnderlyingType(GetType(T)), GetType(T))
            Return DirectCast(Convert.ChangeType(value, target, Globalization.CultureInfo.InvariantCulture), T)
        End Function

        ' ---------------------------------------------------------------- DataRow readers (DBNull-safe)
        Public Shared Function Str(row As DataRow, column As String) As String
            Dim v = row(column)
            Return If(TypeOf v Is DBNull, Nothing, Convert.ToString(v, Globalization.CultureInfo.InvariantCulture))
        End Function

        Public Shared Function Int(row As DataRow, column As String) As Integer
            Dim v = row(column)
            Return If(TypeOf v Is DBNull, 0, Convert.ToInt32(v, Globalization.CultureInfo.InvariantCulture))
        End Function

        Public Shared Function Bool(row As DataRow, column As String) As Boolean
            Dim v = row(column)
            Return Not (TypeOf v Is DBNull) AndAlso Convert.ToInt64(v, Globalization.CultureInfo.InvariantCulture) <> 0
        End Function

        Public Shared Function NullableDate(row As DataRow, column As String) As DateTime?
            Dim v = row(column)
            If TypeOf v Is DBNull Then Return Nothing
            Return Convert.ToDateTime(v, Globalization.CultureInfo.InvariantCulture)
        End Function

        ' ---------------------------------------------------------------- transactions
        ''' <summary>
        ''' Runs <paramref name="work"/> in one DB transaction: commits if it returns normally, rolls back on any
        ''' exception (then rethrows). Use for every multi-statement save (e.g. a sale).
        ''' </summary>
        Public Shared Function InTransaction(Of T)(work As Func(Of MySqlConnection, MySqlTransaction, T)) As T
            Using conn = Open()
                Using tx = conn.BeginTransaction(IsolationLevel.ReadCommitted)
                    Try
                        Dim result = work(conn, tx)
                        tx.Commit()
                        Return result
                    Catch
                        Try
                            tx.Rollback()
                        Catch rollbackError As Exception
                            AppLog.Write(rollbackError, "Rollback failed")
                        End Try
                        Throw
                    End Try
                End Using
            End Using
        End Function

        Public Shared Sub InTransaction(work As Action(Of MySqlConnection, MySqlTransaction))
            InTransaction(Of Boolean)(Function(conn, tx)
                                          work(conn, tx)
                                          Return True
                                      End Function)
        End Sub

        ' ---------------------------------------------------------------- document numbers
        ''' <summary>
        ''' Next document number, e.g. NextDocumentNo(conn, tx, "BK", 4) = "BK-2026-0009". Must run inside the same
        ''' transaction as the insert that uses it; the doc_sequences row lock keeps numbers unique and gap-free
        ''' unless that transaction rolls back.
        ''' </summary>
        Public Shared Function NextDocumentNo(conn As MySqlConnection, tx As MySqlTransaction, prefix As String, digits As Integer) As String
            Dim year = DateTime.Now.Year
            Const sql As String =
                "INSERT INTO doc_sequences (doc_type, seq_year, last_no) VALUES (@type, @year, LAST_INSERT_ID(1)) " &
                "ON DUPLICATE KEY UPDATE last_no = LAST_INSERT_ID(last_no + 1); " &
                "SELECT LAST_INSERT_ID();"
            Using cmd = Command(conn, sql, tx, P("@type", prefix), P("@year", year))
                Dim n = ConvertValue(Of Long)(cmd.ExecuteScalar())
                Return $"{prefix}-{year}-{n.ToString(New String("0"c, digits))}"
            End Using
        End Function

        ' ---------------------------------------------------------------- diagnostics
        ''' <summary>Opens a connection and reads the server version. Returns False with a friendly message on failure.</summary>
        Public Shared Function TestConnection(ByRef message As String) As Boolean
            Try
                Using conn = Open(), cmd = Command(conn, "SELECT VERSION()", Nothing)
                    message = $"Connected to {conn.Database} on {conn.DataSource} (server {cmd.ExecuteScalar()})."
                    Return True
                End Using
            Catch ex As Exception
                AppLog.Write(ex, "Db.TestConnection")
                message = FriendlyMessage(ex)
                Return False
            End Try
        End Function

        ''' <summary>A message safe to show users. Technical details go to the log, never to the screen.</summary>
        Public Shared Function FriendlyMessage(ex As Exception) As String
            Dim mysql = TryCast(ex, MySqlException)
            If mysql Is Nothing AndAlso ex IsNot Nothing Then mysql = TryCast(ex.InnerException, MySqlException)
            If TypeOf ex Is UnauthorizedAccessException Then
                Return "Your account doesn't have permission to do that. Ask the Admin if you need access."
            End If
            If TypeOf ex Is ConfigurationErrorsException Then
                Return "The database settings are missing. Please reinstall LENSYNC or contact the administrator."
            End If
            If mysql Is Nothing Then Return "An unexpected error occurred. The details were saved to the log file."
            Select Case mysql.Number
                Case 1042, 0
                    Return "Cannot connect to the database server. Make sure XAMPP is open and MySQL is started, then try again."
                Case 1045
                    Return "The database refused the login. Please check the connection settings in App.config."
                Case 1049
                    Return "The LENSYNC database (lensync_db) was not found. Please import database\schema.sql first."
                Case 1062
                    Return "That record already exists."
                Case 1205, 1213
                    Return "The database is busy. Please try again."
                Case Else
                    Return "A database error occurred. The details were saved to the log file."
            End Select
        End Function

    End Class

End Namespace
