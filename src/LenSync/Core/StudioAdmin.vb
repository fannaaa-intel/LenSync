Option Strict On
Option Explicit On
Option Infer On

Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Text.RegularExpressions
Imports LenSync.Models

Namespace Core

    Public Class StudioInfo
        Public Property BusinessName As String = ""
        Public Property Address As String = ""
        Public Property Contact As String = ""
        Public Property LogoPath As String = ""
        Public Property SignaturePath As String = ""
    End Class

    Public Class EmailSettings
        Public Property Host As String = "smtp.gmail.com"
        Public Property Port As Integer = 587
        Public Property UseSsl As Boolean = True
        Public Property User As String = ""
        Public Property FromName As String = "LENSYNC"
        ''' <summary>True if a password is stored. The password itself is never sent back to the UI.</summary>
        Public Property HasPassword As Boolean
    End Class

    ''' <summary>Settings > Studio (business info, logo, signature) and Settings > Email (SMTP).</summary>
    Public NotInheritable Class StudioAdmin

        Private Sub New()
        End Sub

        Private Const MaxImageBytes As Long = 5L * 1024 * 1024
        Private Shared ReadOnly EmailPattern As New Regex("^[^@\s]+@[^@\s]+\.[^@\s]+$")
        Private Shared ReadOnly ContactPattern As New Regex("^[0-9+()\- ]{0,40}$")

        ''' <summary>
        ''' Logo/signature files live in %ProgramData%\LenSync\branding so every Windows user on this PC sees them;
        ''' the settings table stores the path.
        ''' </summary>
        Public Shared ReadOnly Property BrandingFolder As String
            Get
                Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LenSync", "branding")
            End Get
        End Property

        ' ================================================================ studio
        Public Shared Function LoadStudio() As StudioInfo
            Session.Demand(Permissions.Settings)
            Return New StudioInfo With {
                .BusinessName = AppSettings.GetValue(AppSettings.BusinessName),
                .Address = AppSettings.GetValue(AppSettings.Address),
                .Contact = AppSettings.GetValue(AppSettings.Contact),
                .LogoPath = AppSettings.GetValue(AppSettings.LogoPath),
                .SignaturePath = AppSettings.GetValue(AppSettings.SignaturePath)}
        End Function

        ''' <summary>
        ''' Saves business details. For each image: Nothing = keep, "" = remove, a file path = import that picture
        ''' (resized and copied into the branding folder).
        ''' </summary>
        Public Shared Function SaveStudio(info As StudioInfo, logoSource As String, signatureSource As String) As AdminResult
            Session.Demand(Permissions.Settings)
            Dim name = If(info.BusinessName, "").Trim()
            Dim address = If(info.Address, "").Trim()
            Dim contact = If(info.Contact, "").Trim()
            Dim result As New AdminResult()
            If name.Length < 2 Then result.AddError("name", "Enter the studio's business name.")
            If name.Length > 120 Then result.AddError("name", "Use at most 120 characters.")
            If address.Length > 255 Then result.AddError("address", "Use at most 255 characters.")
            If Not ContactPattern.IsMatch(contact) Then result.AddError("contact", "Use numbers, spaces, +, - or ( ) only.")
            Dim problem As String = Nothing
            If Not String.IsNullOrEmpty(logoSource) AndAlso Not ImageProblem(logoSource, problem) Then result.AddError("logo", problem)
            If Not String.IsNullOrEmpty(signatureSource) AndAlso Not ImageProblem(signatureSource, problem) Then result.AddError("signature", problem)
            If Not result.Ok Then Return result

            Dim before = LoadStudio()
            Dim newLogo = ApplyImage(logoSource, before.LogoPath, "logo", 600, 600)
            Dim newSignature = ApplyImage(signatureSource, before.SignaturePath, "signature", 600, 240)

            AppSettings.SetValue(AppSettings.BusinessName, name)
            AppSettings.SetValue(AppSettings.Address, address)
            AppSettings.SetValue(AppSettings.Contact, contact)
            AppSettings.SetValue(AppSettings.LogoPath, newLogo)
            AppSettings.SetValue(AppSettings.SignaturePath, newSignature)

            Dim changes As New List(Of String)
            If before.BusinessName <> name Then changes.Add($"name '{before.BusinessName}' -> '{name}'")
            If before.Address <> address Then changes.Add("address changed")
            If before.Contact <> contact Then changes.Add("contact changed")
            If logoSource IsNot Nothing Then changes.Add(If(logoSource.Length = 0, "logo removed", "logo replaced"))
            If signatureSource IsNot Nothing Then changes.Add(If(signatureSource.Length = 0, "signature removed", "signature replaced"))
            If changes.Count > 0 Then AuditLog.Write(AuditActions.StudioUpdate, "settings", Nothing, String.Join("; ", changes))
            Return AdminResult.Success()
        End Function

        Private Shared Function ImageProblem(path As String, ByRef problem As String) As Boolean
            Try
                Dim fi As New FileInfo(path)
                If Not fi.Exists Then
                    problem = "That picture can't be found anymore. Choose it again."
                    Return False
                End If
                If fi.Length > MaxImageBytes Then
                    problem = "Choose a picture smaller than 5 MB."
                    Return False
                End If
                Using img = LoadImageCopy(path)
                    If img.Width < 16 OrElse img.Height < 16 Then
                        problem = "That picture is too small."
                        Return False
                    End If
                End Using
                Return True
            Catch ex As Exception
                AppLog.Write(ex, "Checking image " & path)
                problem = "That file isn't a picture LENSYNC can read. Use PNG or JPG."
                Return False
            End Try
        End Function

        ''' <summary>Returns the path to store: unchanged, "" (removed), or the newly imported file.</summary>
        Private Shared Function ApplyImage(source As String, currentPath As String, prefix As String, maxW As Integer, maxH As Integer) As String
            If source Is Nothing Then Return currentPath
            Dim result = ""
            If source.Length > 0 Then
                Directory.CreateDirectory(BrandingFolder)
                result = Path.Combine(BrandingFolder, $"{prefix}-{DateTime.Now:yyyyMMddHHmmss}.png")
                Using src = LoadImageCopy(source)
                    Dim scale = Math.Min(1.0, Math.Min(maxW / CDbl(src.Width), maxH / CDbl(src.Height)))
                    Dim w = Math.Max(1, CInt(src.Width * scale))
                    Dim h = Math.Max(1, CInt(src.Height * scale))
                    Using bmp As New Bitmap(w, h, PixelFormat.Format32bppArgb)
                        Using g = Graphics.FromImage(bmp)
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality
                            g.DrawImage(src, 0, 0, w, h)
                        End Using
                        bmp.Save(result, ImageFormat.Png)
                    End Using
                End Using
            End If
            ' Remove the old copy (only files we own in the branding folder).
            If Not String.IsNullOrEmpty(currentPath) AndAlso currentPath <> result AndAlso
               currentPath.StartsWith(BrandingFolder, StringComparison.OrdinalIgnoreCase) Then
                Try
                    File.Delete(currentPath)
                Catch ex As IOException
                    AppLog.Write(ex, "Removing old branding image")
                End Try
            End If
            Return result
        End Function

        ''' <summary>Loads a picture without keeping the file locked (so it can be replaced or deleted later).</summary>
        Public Shared Function LoadImageCopy(path As String) As Image
            Dim bytes = File.ReadAllBytes(path)
            Using ms As New MemoryStream(bytes)
                Using img = Image.FromStream(ms)
                    Return New Bitmap(img)
                End Using
            End Using
        End Function

        ' ================================================================ email
        Public Shared Function LoadEmail() As EmailSettings
            Session.Demand(Permissions.Settings)
            Dim port As Integer
            If Not Integer.TryParse(AppSettings.GetValue(AppSettings.SmtpPort, "587"), port) Then port = 587
            Return New EmailSettings With {
                .Host = AppSettings.GetValue(AppSettings.SmtpHost, "smtp.gmail.com"),
                .Port = port,
                .UseSsl = Not String.Equals(AppSettings.GetValue(AppSettings.SmtpEnableSsl, "true"), "false", StringComparison.OrdinalIgnoreCase),
                .User = AppSettings.GetValue(AppSettings.SmtpUser),
                .FromName = AppSettings.GetValue(AppSettings.SmtpFromName, "LENSYNC"),
                .HasPassword = AppSettings.GetSecret(AppSettings.SmtpPassword).Length > 0}
        End Function

        ''' <summary><paramref name="newPassword"/>: Nothing or "" keeps the stored App Password.</summary>
        Public Shared Function SaveEmail(host As String, portText As String, useSsl As Boolean, user As String,
                                         fromName As String, newPassword As String) As AdminResult
            Session.Demand(Permissions.Settings)
            host = If(host, "").Trim()
            user = If(user, "").Trim()
            fromName = If(fromName, "").Trim()
            Dim result As New AdminResult()
            If host.Length = 0 OrElse host.Contains(" ") Then result.AddError("host", "Enter the mail server, e.g. smtp.gmail.com.")
            Dim port As Integer
            If Not Integer.TryParse(If(portText, "").Trim(), port) OrElse port < 1 OrElse port > 65535 Then
                result.AddError("port", "Use a port number such as 587.")
            End If
            If user.Length = 0 OrElse Not EmailPattern.IsMatch(user) Then result.AddError("user", "Enter the Gmail address that sends the emails.")
            If fromName.Length = 0 Then result.AddError("fromName", "Enter the sender name customers and staff will see.")
            If fromName.Length > 80 Then result.AddError("fromName", "Use at most 80 characters.")
            Dim hasStored = AppSettings.GetSecret(AppSettings.SmtpPassword).Length > 0
            If String.IsNullOrEmpty(newPassword) AndAlso Not hasStored Then result.AddError("password", "Enter the 16-letter Gmail App Password.")
            If Not result.Ok Then Return result

            AppSettings.SetValue(AppSettings.SmtpHost, host)
            AppSettings.SetValue(AppSettings.SmtpPort, port.ToString())
            AppSettings.SetValue(AppSettings.SmtpEnableSsl, If(useSsl, "true", "false"))
            AppSettings.SetValue(AppSettings.SmtpUser, user)
            AppSettings.SetValue(AppSettings.SmtpFromName, fromName)
            If Not String.IsNullOrEmpty(newPassword) Then AppSettings.SetSecret(AppSettings.SmtpPassword, newPassword.Trim())
            AuditLog.Write(AuditActions.EmailSettingsUpdate, "settings", Nothing,
                           $"SMTP {host}:{port}, sender {user}" & If(String.IsNullOrEmpty(newPassword), "", "; app password replaced"))
            Return AdminResult.Success()
        End Function

        ''' <summary>Sends a test message to the studio's own Gmail address using the saved settings. Blocking.</summary>
        Public Shared Function SendTestEmail() As AdminResult
            Session.Demand(Permissions.Settings)
            If Not Mailer.IsConfigured() Then Return AdminResult.Fail("", "Save the email settings first.")
            Dim toAddress = AppSettings.GetValue(AppSettings.SmtpUser)
            Dim studio = AppSettings.GetValue(AppSettings.BusinessName, "LENSYNC")
            Try
                Mailer.Send(toAddress, "LENSYNC test email",
                            $"This is a test from LENSYNC ({studio}). Email is set up correctly: password reset codes can now be sent.",
                            Nothing)
            Catch ex As Exception
                AppLog.Write(ex, "Sending test email")
                AuditLog.Write(AuditActions.EmailTest, "settings", Nothing, "Test email failed")
                Return AdminResult.Fail("", "The test email could not be sent. Check the Gmail address, the App Password and the internet connection.")
            End Try
            AuditLog.Write(AuditActions.EmailTest, "settings", Nothing, $"Test email sent to {toAddress}")
            Return AdminResult.Success(toAddress)
        End Function

    End Class

End Namespace
