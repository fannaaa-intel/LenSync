Option Strict On
Option Explicit On
Option Infer On

Imports MySql.Data.MySqlClient

Namespace Core

    ''' <summary>audit_logs.action values.</summary>
    Public NotInheritable Class AuditActions

        Private Sub New()
        End Sub

        Public Const Login As String = "LOGIN"
        Public Const LoginFailed As String = "LOGIN_FAILED"
        Public Const LoginBlocked As String = "LOGIN_BLOCKED"
        Public Const AccountLocked As String = "ACCOUNT_LOCKED"
        Public Const Logout As String = "LOGOUT"
        Public Const PasswordChanged As String = "PASSWORD_CHANGED"
        Public Const PasswordResetRequested As String = "PASSWORD_RESET_REQUESTED"
        Public Const PasswordResetCodeFailed As String = "PASSWORD_RESET_CODE_FAILED"
        Public Const PasswordReset As String = "PASSWORD_RESET"
        Public Const AccessDenied As String = "ACCESS_DENIED"
        Public Const UserCreate As String = "USER_CREATE"
        Public Const UserUpdate As String = "USER_UPDATE"
        Public Const UserDeactivate As String = "USER_DEACTIVATE"
        Public Const UserActivate As String = "USER_ACTIVATE"
        Public Const PasswordResetByAdmin As String = "PASSWORD_RESET_BY_ADMIN"
        Public Const RolePermissionsUpdate As String = "ROLE_PERMISSIONS_UPDATE"
        Public Const PackageCreate As String = "PACKAGE_CREATE"
        Public Const PackageUpdate As String = "PACKAGE_UPDATE"
        Public Const PackageDeactivate As String = "PACKAGE_DEACTIVATE"
        Public Const PackageActivate As String = "PACKAGE_ACTIVATE"
        Public Const AddonCreate As String = "ADDON_CREATE"
        Public Const AddonUpdate As String = "ADDON_UPDATE"
        Public Const AddonDeactivate As String = "ADDON_DEACTIVATE"
        Public Const AddonActivate As String = "ADDON_ACTIVATE"
        Public Const StudioUpdate As String = "STUDIO_UPDATE"
        Public Const EmailSettingsUpdate As String = "EMAIL_SETTINGS_UPDATE"
        Public Const EmailTest As String = "EMAIL_TEST"
        Public Const BookingCreate As String = "BOOKING_CREATE"
        Public Const BookingUpdate As String = "BOOKING_UPDATE"
        Public Const BookingDone As String = "BOOKING_DONE"
        Public Const BookingCancel As String = "BOOKING_CANCEL"
        Public Const SaleComplete As String = "SALE_COMPLETE"
        Public Const Discount As String = "DISCOUNT"
        Public Const VoidSale As String = "VOID"
        Public Const ReceiptPrint As String = "RECEIPT_PRINT"

    End Class

    ''' <summary>
    ''' Append-only audit trail. Never put passwords, reset codes or hashes in <c>details</c>.
    ''' </summary>
    Public NotInheritable Class AuditLog

        Private Const InsertSql As String =
            "INSERT INTO audit_logs (user_id, action, entity, entity_id, details) VALUES (@user, @action, @entity, @entityId, @details)"
        Private Const MaxDetails As Integer = 2000

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Best-effort write on its own connection: an audit failure is logged to file but never blocks the user.
        ''' <paramref name="userId"/> defaults to the signed-in user.
        ''' </summary>
        Public Shared Sub Write(action As String, Optional entity As String = Nothing, Optional entityId As Integer? = Nothing,
                                Optional details As String = Nothing, Optional userId As Integer? = Nothing)
            Try
                Db.Execute(InsertSql, Parameters(action, entity, entityId, details, userId))
            Catch ex As Exception
                AppLog.Write(ex, $"Audit write failed ({action})")
            End Try
        End Sub

        ''' <summary>Transactional write: part of the caller's DB transaction (e.g. a sale), so it throws on failure.</summary>
        Public Shared Sub Write(conn As MySqlConnection, tx As MySqlTransaction, action As String,
                                Optional entity As String = Nothing, Optional entityId As Integer? = Nothing,
                                Optional details As String = Nothing, Optional userId As Integer? = Nothing)
            Using cmd = Db.Command(conn, InsertSql, tx, Parameters(action, entity, entityId, details, userId))
                cmd.ExecuteNonQuery()
            End Using
        End Sub

        Private Shared Function Parameters(action As String, entity As String, entityId As Integer?,
                                           details As String, userId As Integer?) As MySqlParameter()
            Dim uid As Integer? = userId
            If Not uid.HasValue AndAlso Session.IsSignedIn Then uid = Session.CurrentUser.Id
            If details IsNot Nothing AndAlso details.Length > MaxDetails Then details = details.Substring(0, MaxDetails)
            Return {
                Db.P("@user", If(uid.HasValue, CObj(uid.Value), Nothing)),
                Db.P("@action", action),
                Db.P("@entity", entity),
                Db.P("@entityId", If(entityId.HasValue, CObj(entityId.Value), Nothing)),
                Db.P("@details", details)
            }
        End Function

    End Class

End Namespace
