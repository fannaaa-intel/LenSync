Option Strict On
Option Explicit On
Option Infer On

Imports System.Data
Imports MySql.Data.MySqlClient
Imports LenSync.Core
Imports LenSync.Models

Namespace Data

    ''' <summary>SQL for the users table (sign-in, lockout, passwords, permissions).</summary>
    Public NotInheritable Class UserRepository

        ' Lock state is computed with the database clock so app and DB never disagree about "now".
        Private Const SelectUser As String =
            "SELECT u.id, u.username, u.password_hash, u.full_name, u.email, u.role_id, r.name AS role_name, " &
            "       u.is_active, u.must_change_password, u.failed_attempts, u.last_login, " &
            "       (u.locked_until IS NOT NULL AND u.locked_until > NOW()) AS is_locked, " &
            "       (u.locked_until IS NOT NULL AND u.locked_until <= NOW()) AS lock_expired, " &
            "       GREATEST(0, IFNULL(TIMESTAMPDIFF(SECOND, NOW(), u.locked_until), 0)) AS lock_seconds " &
            "FROM users u JOIN roles r ON r.id = u.role_id "

        Private Sub New()
        End Sub

        Public Shared Function FindByUsername(username As String) As User
            Dim t = Db.Query(SelectUser & "WHERE u.username = @username LIMIT 1", Db.P("@username", username))
            Return If(t.Rows.Count = 0, Nothing, Map(t.Rows(0)))
        End Function

        Public Shared Function GetById(id As Integer) As User
            Dim t = Db.Query(SelectUser & "WHERE u.id = @id", Db.P("@id", id))
            Return If(t.Rows.Count = 0, Nothing, Map(t.Rows(0)))
        End Function

        Private Shared Function Map(row As DataRow) As User
            Return New User With {
                .Id = Db.Int(row, "id"),
                .Username = Db.Str(row, "username"),
                .PasswordHash = Db.Str(row, "password_hash"),
                .FullName = Db.Str(row, "full_name"),
                .Email = Db.Str(row, "email"),
                .RoleId = Db.Int(row, "role_id"),
                .RoleName = Db.Str(row, "role_name"),
                .IsActive = Db.Bool(row, "is_active"),
                .MustChangePassword = Db.Bool(row, "must_change_password"),
                .FailedAttempts = Db.Int(row, "failed_attempts"),
                .LastLogin = Db.NullableDate(row, "last_login"),
                .IsLocked = Db.Bool(row, "is_locked"),
                .LockExpired = Db.Bool(row, "lock_expired"),
                .LockSecondsLeft = Db.Int(row, "lock_seconds")
            }
        End Function

        Public Shared Function GetPermissionKeys(roleId As Integer) As List(Of String)
            Dim t = Db.Query("SELECT p.perm_key FROM role_permissions rp JOIN permissions p ON p.id = rp.permission_id " &
                             "WHERE rp.role_id = @role", Db.P("@role", roleId))
            Return t.Rows.Cast(Of DataRow)().Select(Function(r) Db.Str(r, "perm_key")).ToList()
        End Function

        ' ---------------------------------------------------------------- lockout
        ''' <summary>A lock that has run out starts a fresh count of attempts.</summary>
        Public Shared Sub ClearExpiredLock(userId As Integer)
            Db.Execute("UPDATE users SET failed_attempts = 0, locked_until = NULL " &
                       "WHERE id = @id AND locked_until IS NOT NULL AND locked_until <= NOW()", Db.P("@id", userId))
        End Sub

        ''' <summary>Counts a wrong password; locks the account when the limit is reached. Returns the new state.</summary>
        Public Shared Function RegisterFailedLogin(userId As Integer) As (Attempts As Integer, Locked As Boolean)
            ' locked_until is assigned first so it reads the OLD failed_attempts in every SQL mode.
            Const sql As String =
                "UPDATE users SET " &
                "  locked_until = IF(failed_attempts + 1 >= @max, DATE_ADD(NOW(), INTERVAL @mins MINUTE), locked_until), " &
                "  failed_attempts = failed_attempts + 1 " &
                "WHERE id = @id; " &
                "SELECT failed_attempts, (locked_until IS NOT NULL AND locked_until > NOW()) AS is_locked FROM users WHERE id = @id;"
            Dim t = Db.Query(sql, Db.P("@id", userId), Db.P("@max", Passwords.MaxFailedAttempts), Db.P("@mins", Passwords.LockMinutes))
            Dim row = t.Rows(0)
            Return (Db.Int(row, "failed_attempts"), Db.Bool(row, "is_locked"))
        End Function

        Public Shared Sub RegisterSuccessfulLogin(userId As Integer)
            Db.Execute("UPDATE users SET failed_attempts = 0, locked_until = NULL, last_login = NOW() WHERE id = @id",
                       Db.P("@id", userId))
        End Sub

        ' ---------------------------------------------------------------- administration (Settings > Users)
        Public Shared Function ListSummaries() As List(Of UserSummary)
            Dim t = Db.Query(
                "SELECT u.id, u.username, u.full_name, IFNULL(u.email, '') AS email, u.role_id, r.name AS role_name, u.is_active, " &
                "       u.must_change_password, u.last_login, (u.locked_until IS NOT NULL AND u.locked_until > NOW()) AS is_locked " &
                "FROM users u JOIN roles r ON r.id = u.role_id ORDER BY u.is_active DESC, u.full_name")
            Return t.Rows.Cast(Of DataRow)().Select(
                Function(r) New UserSummary With {
                    .Id = Db.Int(r, "id"), .Username = Db.Str(r, "username"), .FullName = Db.Str(r, "full_name"),
                    .Email = Db.Str(r, "email"), .RoleId = Db.Int(r, "role_id"), .RoleName = Db.Str(r, "role_name"),
                    .IsActive = Db.Bool(r, "is_active"), .MustChangePassword = Db.Bool(r, "must_change_password"),
                    .LastLogin = Db.NullableDate(r, "last_login"), .IsLocked = Db.Bool(r, "is_locked")}).ToList()
        End Function

        Public Shared Function UsernameExists(username As String) As Boolean
            Return Db.Scalar(Of Integer)("SELECT COUNT(*) FROM users WHERE username = @u", Db.P("@u", username)) > 0
        End Function

        ''' <summary>Active users in the Admin role, not counting <paramref name="excludeUserId"/>.</summary>
        Public Shared Function CountOtherActiveAdmins(excludeUserId As Integer) As Integer
            Return Db.Scalar(Of Integer)(
                "SELECT COUNT(*) FROM users u JOIN roles r ON r.id = u.role_id " &
                "WHERE r.name = @admin AND u.is_active = 1 AND u.id <> @id",
                Db.P("@admin", Role.AdminName), Db.P("@id", excludeUserId))
        End Function

        Public Shared Function Insert(conn As MySqlConnection, tx As MySqlTransaction, username As String, fullName As String,
                                      email As String, roleId As Integer, passwordHash As String) As Integer
            Using cmd = Db.Command(conn,
                "INSERT INTO users (username, password_hash, full_name, email, role_id, is_active, must_change_password) " &
                "VALUES (@u, @h, @n, @e, @r, 1, 1); SELECT LAST_INSERT_ID();", tx,
                Db.P("@u", username), Db.P("@h", passwordHash), Db.P("@n", fullName), Db.P("@e", email), Db.P("@r", roleId))
                Return Db.ConvertValue(Of Integer)(cmd.ExecuteScalar())
            End Using
        End Function

        Public Shared Sub UpdateProfile(conn As MySqlConnection, tx As MySqlTransaction, id As Integer, fullName As String,
                                        email As String, roleId As Integer)
            Using cmd = Db.Command(conn, "UPDATE users SET full_name = @n, email = @e, role_id = @r WHERE id = @id", tx,
                                   Db.P("@n", fullName), Db.P("@e", email), Db.P("@r", roleId), Db.P("@id", id))
                cmd.ExecuteNonQuery()
            End Using
        End Sub

        Public Shared Sub SetActive(conn As MySqlConnection, tx As MySqlTransaction, id As Integer, active As Boolean)
            Using cmd = Db.Command(conn, "UPDATE users SET is_active = @a WHERE id = @id", tx,
                                   Db.P("@a", If(active, 1, 0)), Db.P("@id", id))
                cmd.ExecuteNonQuery()
            End Using
        End Sub

        ''' <summary>Admin reset: temporary password, must change it at next log-in, lock cleared.</summary>
        Public Shared Sub SetTemporaryPassword(conn As MySqlConnection, tx As MySqlTransaction, id As Integer, hash As String)
            Using cmd = Db.Command(conn,
                "UPDATE users SET password_hash = @h, must_change_password = 1, failed_attempts = 0, locked_until = NULL WHERE id = @id",
                tx, Db.P("@h", hash), Db.P("@id", id))
                cmd.ExecuteNonQuery()
            End Using
        End Sub

        ' ---------------------------------------------------------------- passwords
        Public Shared Function GetPasswordHash(userId As Integer) As String
            Return Db.Scalar(Of String)("SELECT password_hash FROM users WHERE id = @id", Db.P("@id", userId))
        End Function

        ''' <summary>Stores a new hash, clears the first-login flag and any lock.</summary>
        Public Shared Sub UpdatePassword(conn As MySqlConnection, tx As MySqlTransaction, userId As Integer, newHash As String)
            Using cmd = Db.Command(conn,
                "UPDATE users SET password_hash = @hash, must_change_password = 0, failed_attempts = 0, locked_until = NULL " &
                "WHERE id = @id", tx, Db.P("@hash", newHash), Db.P("@id", userId))
                cmd.ExecuteNonQuery()
            End Using
        End Sub

    End Class

End Namespace
