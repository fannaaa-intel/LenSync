Option Strict On
Option Explicit On
Option Infer On

Imports MySql.Data.MySqlClient
Imports LenSync.Core
Imports LenSync.Models

Namespace Data

    ''' <summary>SQL for password_resets (emailed 6-digit codes, stored hashed).</summary>
    Public NotInheritable Class PasswordResetRepository

        Private Sub New()
        End Sub

        ''' <summary>Seconds since this user's last code request, or Nothing if they never asked.</summary>
        Public Shared Function SecondsSinceLastRequest(userId As Integer) As Integer?
            Dim t = Db.Query("SELECT TIMESTAMPDIFF(SECOND, MAX(created_at), NOW()) AS secs FROM password_resets WHERE user_id = @u",
                             Db.P("@u", userId))
            Dim v = t.Rows(0)("secs")
            If TypeOf v Is DBNull Then Return Nothing
            Return Convert.ToInt32(v)
        End Function

        ''' <summary>Stores a new code (hash only) and retires any older unused code for the user.</summary>
        Public Shared Function Create(userId As Integer, codeHash As String, validMinutes As Integer) As Integer
            Return Db.InTransaction(Of Integer)(
                Function(conn, tx)
                    Using retire = Db.Command(conn, "UPDATE password_resets SET used_at = NOW() WHERE user_id = @u AND used_at IS NULL",
                                              tx, Db.P("@u", userId))
                        retire.ExecuteNonQuery()
                    End Using
                    Using insert = Db.Command(conn,
                        "INSERT INTO password_resets (user_id, code_hash, expires_at) " &
                        "VALUES (@u, @h, DATE_ADD(NOW(), INTERVAL @m MINUTE)); SELECT LAST_INSERT_ID();",
                        tx, Db.P("@u", userId), Db.P("@h", codeHash), Db.P("@m", validMinutes))
                        Return Db.ConvertValue(Of Integer)(insert.ExecuteScalar())
                    End Using
                End Function)
        End Function

        Public Shared Sub Retire(resetId As Integer)
            Db.Execute("UPDATE password_resets SET used_at = NOW() WHERE id = @id AND used_at IS NULL", Db.P("@id", resetId))
        End Sub

        ''' <summary>The user's newest unused code, or Nothing.</summary>
        Public Shared Function GetLatestUnused(userId As Integer) As PasswordReset
            Dim t = Db.Query(
                "SELECT id, user_id, code_hash, attempts, (expires_at <= NOW()) AS is_expired FROM password_resets " &
                "WHERE user_id = @u AND used_at IS NULL ORDER BY id DESC LIMIT 1", Db.P("@u", userId))
            If t.Rows.Count = 0 Then Return Nothing
            Dim r = t.Rows(0)
            Return New PasswordReset With {
                .Id = Db.Int(r, "id"), .UserId = Db.Int(r, "user_id"), .CodeHash = Db.Str(r, "code_hash"),
                .Attempts = Db.Int(r, "attempts"), .IsExpired = Db.Bool(r, "is_expired")}
        End Function

        ''' <summary>Counts a wrong code; the code dies at the attempt limit. Returns the new attempt count.</summary>
        Public Shared Function RegisterWrongCode(resetId As Integer) As Integer
            Return Db.Scalar(Of Integer)(
                "UPDATE password_resets SET used_at = IF(attempts + 1 >= @max, NOW(), used_at), attempts = attempts + 1 WHERE id = @id; " &
                "SELECT attempts FROM password_resets WHERE id = @id;",
                Db.P("@id", resetId), Db.P("@max", Passwords.ResetMaxAttempts))
        End Function

        ''' <summary>Marks a code used inside the reset transaction. False if it was already used or retired.</summary>
        Public Shared Function MarkUsed(conn As MySqlConnection, tx As MySqlTransaction, resetId As Integer) As Boolean
            Using cmd = Db.Command(conn, "UPDATE password_resets SET used_at = NOW() WHERE id = @id AND used_at IS NULL",
                                   tx, Db.P("@id", resetId))
                Return cmd.ExecuteNonQuery() = 1
            End Using
        End Function

    End Class

End Namespace
