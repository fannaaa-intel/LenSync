Option Strict On
Option Explicit On
Option Infer On

Imports System.Net
Imports System.Net.Mail
Imports System.Text

Namespace Core

    ''' <summary>Sends email through the SMTP account in Settings (Gmail + App Password by default).</summary>
    Public NotInheritable Class Mailer

        Private Sub New()
        End Sub

        Public Shared Function IsConfigured() As Boolean
            Return AppSettings.GetValue(AppSettings.SmtpHost).Length > 0 AndAlso
                   AppSettings.GetValue(AppSettings.SmtpUser).Length > 0 AndAlso
                   AppSettings.GetSecret(AppSettings.SmtpPassword).Length > 0
        End Function

        ''' <summary>Blocking send (call from a background task). Throws on failure.</summary>
        Public Shared Sub Send(toAddress As String, subject As String, textBody As String, htmlBody As String)
            Dim host = AppSettings.GetValue(AppSettings.SmtpHost, "smtp.gmail.com")
            Dim port As Integer
            If Not Integer.TryParse(AppSettings.GetValue(AppSettings.SmtpPort, "587"), port) Then port = 587
            Dim ssl = Not String.Equals(AppSettings.GetValue(AppSettings.SmtpEnableSsl, "true"), "false", StringComparison.OrdinalIgnoreCase)
            Dim user = AppSettings.GetValue(AppSettings.SmtpUser)
            ' Gmail shows App Passwords in groups of four ("abcd efgh ..."); the spaces are not part of it.
            Dim pass = AppSettings.GetSecret(AppSettings.SmtpPassword).Replace(" ", "")
            Dim fromName = AppSettings.GetValue(AppSettings.SmtpFromName, "LENSYNC")

            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12
            Using client As New SmtpClient(host, port)
                client.EnableSsl = ssl
                client.DeliveryMethod = SmtpDeliveryMethod.Network
                client.UseDefaultCredentials = False
                client.Credentials = New NetworkCredential(user, pass)
                client.Timeout = 20000
                Using message As New MailMessage(New MailAddress(user, fromName), New MailAddress(toAddress))
                    message.Subject = subject
                    message.SubjectEncoding = Encoding.UTF8
                    message.BodyEncoding = Encoding.UTF8
                    message.Body = textBody
                    message.IsBodyHtml = False
                    If Not String.IsNullOrEmpty(htmlBody) Then
                        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(htmlBody, Encoding.UTF8, "text/html"))
                    End If
                    client.Send(message)
                End Using
            End Using
        End Sub

        ''' <summary>"maria.santos@gmail.com" -> "m•••••••••s@gmail.com" for on-screen hints.</summary>
        Public Shared Function MaskEmail(email As String) As String
            If String.IsNullOrEmpty(email) Then Return ""
            Dim at = email.IndexOf("@"c)
            If at <= 1 Then Return email
            Dim local = email.Substring(0, at)
            Dim masked = local(0) & New String("•"c, Math.Max(1, local.Length - 2)) & If(local.Length > 2, local(local.Length - 1).ToString(), "")
            Return masked & email.Substring(at)
        End Function

    End Class

End Namespace
