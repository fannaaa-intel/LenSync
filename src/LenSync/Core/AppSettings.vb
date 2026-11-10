Option Strict On
Option Explicit On
Option Infer On

Imports System.Security.Cryptography
Imports System.Text

Namespace Core

    ''' <summary>
    ''' Studio settings stored in the <c>settings</c> table (one studio per installation).
    ''' Secrets (SMTP password) are stored encrypted with Windows DPAPI, prefixed "dpapi:".
    ''' </summary>
    Public NotInheritable Class AppSettings

        Public Const BusinessName As String = "business_name"
        Public Const Address As String = "address"
        Public Const Contact As String = "contact"
        Public Const LogoPath As String = "logo_path"
        Public Const SignaturePath As String = "signature_path"
        Public Const SmtpHost As String = "smtp_host"
        Public Const SmtpPort As String = "smtp_port"
        Public Const SmtpEnableSsl As String = "smtp_enable_ssl"
        Public Const SmtpUser As String = "smtp_user"
        Public Const SmtpPassword As String = "smtp_password"
        Public Const SmtpFromName As String = "smtp_from_name"
        Public Const ReportSecret As String = "report_secret"

        Private Const SecretPrefix As String = "dpapi:"
        ' Extra entropy so other apps using DPAPI on this PC can't trivially decrypt our values.
        Private Shared ReadOnly Entropy As Byte() = Encoding.UTF8.GetBytes("LENSYNC.settings.v1")

        Private Sub New()
        End Sub

        Public Shared Function GetValue(key As String, Optional defaultValue As String = "") As String
            Dim value = Db.Scalar(Of String)("SELECT setting_value FROM settings WHERE setting_key = @key", Db.P("@key", key))
            Return If(String.IsNullOrEmpty(value), defaultValue, value)
        End Function

        Public Shared Sub SetValue(key As String, value As String)
            Db.Execute("INSERT INTO settings (setting_key, setting_value) VALUES (@key, @value) " &
                       "ON DUPLICATE KEY UPDATE setting_value = VALUES(setting_value)",
                       Db.P("@key", key), Db.P("@value", value))
        End Sub

        ''' <summary>Reads a value stored with SetSecret. Plain (legacy/hand-entered) values are returned as-is.</summary>
        Public Shared Function GetSecret(key As String) As String
            Dim stored = GetValue(key)
            If Not stored.StartsWith(SecretPrefix, StringComparison.Ordinal) Then Return stored
            Try
                Dim data = Convert.FromBase64String(stored.Substring(SecretPrefix.Length))
                Return Encoding.UTF8.GetString(ProtectedData.Unprotect(data, Entropy, DataProtectionScope.LocalMachine))
            Catch ex As Exception
                ' Typically: the database was moved to another PC. The admin must re-enter the secret.
                AppLog.Write(ex, $"Could not decrypt setting '{key}'")
                Return ""
            End Try
        End Function

        Public Shared Sub SetSecret(key As String, value As String)
            If String.IsNullOrEmpty(value) Then
                SetValue(key, "")
                Return
            End If
            Dim data = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.LocalMachine)
            SetValue(key, SecretPrefix & Convert.ToBase64String(data))
        End Sub

        ''' <summary>Generates the HMAC key for report verification codes once (first run).</summary>
        Public Shared Sub EnsureReportSecret()
            If String.IsNullOrEmpty(GetValue(ReportSecret)) Then
                SetValue(ReportSecret, Passwords.NewSecret(32))
                AppLog.Info("report_secret generated")
            End If
        End Sub

    End Class

End Namespace
