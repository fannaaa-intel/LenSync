Option Strict On
Option Explicit On
Option Infer On

Imports LenSync.Data
Imports LenSync.Models

Namespace Core

    Public Enum SignInStatus
        Success
        InvalidCredentials
        Locked
        Inactive
    End Enum

    Public NotInheritable Class SignInResult
        Public Property Status As SignInStatus
        Public Property User As User
        ''' <summary>For Locked: seconds until the user may try again.</summary>
        Public Property SecondsLocked As Integer
        ''' <summary>For InvalidCredentials: tries left before the 5-minute lock.</summary>
        Public Property AttemptsLeft As Integer
    End Class

    Public NotInheritable Class ResetRequestResult
        Public Property Ok As Boolean
        Public Property Message As String
        Public Property UserId As Integer
        Public Property MaskedEmail As String
    End Class

    Public NotInheritable Class ResetVerifyResult
        Public Property Ok As Boolean
        Public Property Message As String
        Public Property ResetId As Integer
    End Class

    ''' <summary>
    ''' Sign-in, lockout, sign-out, password change and the emailed-code password reset.
    ''' All security decisions live here (not in forms) so every screen gets the same rules and audit entries.
    ''' </summary>
    Public NotInheritable Class Auth

        Public Const GenericInvalid As String = "Invalid username or password."

        ' Unknown usernames get the same counter/lock behaviour as real ones (kept in memory), so the responses
        ' don't reveal which usernames exist.
        Private Class Strikes
            Public Count As Integer
            Public LockedUntil As DateTime
        End Class
        Private Shared ReadOnly _unknown As New Dictionary(Of String, Strikes)(StringComparer.OrdinalIgnoreCase)

        Private Sub New()
        End Sub

        ' ================================================================ sign in / out
        Public Shared Function SignIn(username As String, password As String) As SignInResult
            username = If(username, "").Trim()
            Dim user = UserRepository.FindByUsername(username)
            If user Is Nothing Then Return FailUnknownUser(username, password)

            If user.LockExpired Then
                UserRepository.ClearExpiredLock(user.Id)
                user.FailedAttempts = 0
            End If
            If user.IsLocked Then
                AuditLog.Write(AuditActions.LoginBlocked, "users", user.Id, "Sign-in attempted while locked", user.Id)
                Return New SignInResult With {.Status = SignInStatus.Locked, .SecondsLocked = Math.Max(1, user.LockSecondsLeft)}
            End If

            If Not Passwords.Verify(password, user.PasswordHash) Then
                Dim state = UserRepository.RegisterFailedLogin(user.Id)
                AuditLog.Write(AuditActions.LoginFailed, "users", user.Id, $"Wrong password (attempt {state.Attempts})", user.Id)
                If state.Locked Then
                    AuditLog.Write(AuditActions.AccountLocked, "users", user.Id,
                                   $"Locked for {Passwords.LockMinutes} minutes after {Passwords.MaxFailedAttempts} failed attempts", user.Id)
                    Return New SignInResult With {.Status = SignInStatus.Locked, .SecondsLocked = Passwords.LockMinutes * 60}
                End If
                Return New SignInResult With {.Status = SignInStatus.InvalidCredentials,
                                              .AttemptsLeft = Math.Max(0, Passwords.MaxFailedAttempts - state.Attempts)}
            End If

            ' Only after the correct password do we say the account is deactivated (no information for guessers).
            If Not user.IsActive Then
                AuditLog.Write(AuditActions.LoginFailed, "users", user.Id, "Account is deactivated", user.Id)
                Return New SignInResult With {.Status = SignInStatus.Inactive}
            End If

            UserRepository.RegisterSuccessfulLogin(user.Id)
            Session.Start(user, UserRepository.GetPermissionKeys(user.RoleId))
            AuditLog.Write(AuditActions.Login, "users", user.Id, $"Logged in ({user.RoleName})")
            Return New SignInResult With {.Status = SignInStatus.Success, .User = user}
        End Function

        Private Shared Function FailUnknownUser(username As String, password As String) As SignInResult
            Passwords.DummyVerify(password)
            Dim key = If(username.Length > 50, username.Substring(0, 50), username)
            Dim s As Strikes = Nothing
            If Not _unknown.TryGetValue(key, s) Then
                s = New Strikes()
                _unknown(key) = s
            End If
            If s.LockedUntil > DateTime.Now Then
                Return New SignInResult With {.Status = SignInStatus.Locked,
                                              .SecondsLocked = Math.Max(1, CInt((s.LockedUntil - DateTime.Now).TotalSeconds))}
            End If
            If s.LockedUntil <> DateTime.MinValue Then
                s.Count = 0
                s.LockedUntil = DateTime.MinValue
            End If
            s.Count += 1
            AuditLog.Write(AuditActions.LoginFailed, "users", Nothing, $"Unknown username: {key}")
            If s.Count >= Passwords.MaxFailedAttempts Then
                s.LockedUntil = DateTime.Now.AddMinutes(Passwords.LockMinutes)
                Return New SignInResult With {.Status = SignInStatus.Locked, .SecondsLocked = Passwords.LockMinutes * 60}
            End If
            Return New SignInResult With {.Status = SignInStatus.InvalidCredentials, .AttemptsLeft = Passwords.MaxFailedAttempts - s.Count}
        End Function

        Public Shared Sub SignOut(Optional reason As String = "Logged out")
            If Not Session.IsSignedIn Then Return
            AuditLog.Write(AuditActions.Logout, "users", Session.CurrentUser.Id, reason)
            Session.Clear()
        End Sub

        ' ================================================================ change password (signed in)
        ''' <summary>
        ''' Changes the signed-in user's password. <paramref name="currentPassword"/> may be Nothing only for the forced
        ''' first-login change (the user has just proven it). Returns a user-facing problem, or Nothing on success.
        ''' </summary>
        Public Shared Function ChangePassword(currentPassword As String, newPassword As String) As String
            If Not Session.IsSignedIn Then Return "Please log in again."
            Dim user = Session.CurrentUser
            Dim storedHash = UserRepository.GetPasswordHash(user.Id)
            If currentPassword IsNot Nothing AndAlso Not Passwords.Verify(currentPassword, storedHash) Then
                Return "The current password is incorrect."
            End If
            Dim problem = Passwords.Problem(newPassword, user.Username)
            If problem IsNot Nothing Then Return problem
            If Passwords.Verify(newPassword, storedHash) Then Return "Choose a password different from the current one."

            Dim newHash = Passwords.Hash(newPassword)
            Db.InTransaction(
                Sub(conn, tx)
                    UserRepository.UpdatePassword(conn, tx, user.Id, newHash)
                    AuditLog.Write(conn, tx, AuditActions.PasswordChanged, "users", user.Id,
                                   If(currentPassword Is Nothing, "First sign-in password change", "Changed own password"))
                End Sub)
            user.MustChangePassword = False
            Return Nothing
        End Function

        ' ================================================================ forgot password
        ''' <summary>Step 1: username + registered email -> 6-digit code by email. Blocking (sends mail); run off the UI thread.</summary>
        Public Shared Function RequestResetCode(username As String, email As String) As ResetRequestResult
            username = If(username, "").Trim()
            email = If(email, "").Trim()
            Dim user = UserRepository.FindByUsername(username)
            If user Is Nothing OrElse Not user.IsActive OrElse String.IsNullOrWhiteSpace(user.Email) OrElse
               Not String.Equals(user.Email.Trim(), email, StringComparison.OrdinalIgnoreCase) Then
                AuditLog.Write(AuditActions.PasswordResetRequested, "users", user?.Id, "Rejected: username and email do not match",
                               If(user Is Nothing, Nothing, CType(user.Id, Integer?)))
                Return Fail("The username and email do not match an active account. Check both, or ask the Admin to reset your password.")
            End If
            If Not Mailer.IsConfigured() Then
                Return Fail("Email sending is not set up yet. Please ask the Admin to reset your password in Settings.")
            End If
            Dim since = PasswordResetRepository.SecondsSinceLastRequest(user.Id)
            If since.HasValue AndAlso since.Value < Passwords.ResetResendSeconds Then
                Return Fail($"A code was just sent. Please wait {Passwords.ResetResendSeconds - since.Value} seconds before asking again.")
            End If

            Dim code = Passwords.NewResetCode()
            Dim resetId = PasswordResetRepository.Create(user.Id, Passwords.HashResetCode(code), Passwords.ResetCodeMinutes)
            Try
                SendResetEmail(user, code)
            Catch ex As Exception
                PasswordResetRepository.Retire(resetId)
                AppLog.Write(ex, "Sending password reset email")
                Return Fail("The email could not be sent. Check the internet connection and try again, or ask the Admin to reset your password.")
            End Try
            AuditLog.Write(AuditActions.PasswordResetRequested, "users", user.Id, "Reset code emailed", user.Id)
            Return New ResetRequestResult With {.Ok = True, .UserId = user.Id, .MaskedEmail = Mailer.MaskEmail(user.Email)}
        End Function

        Private Shared Function Fail(message As String) As ResetRequestResult
            Return New ResetRequestResult With {.Ok = False, .Message = message}
        End Function

        Private Shared Sub SendResetEmail(user As User, code As String)
            Dim studio = AppSettings.GetValue(AppSettings.BusinessName, "LENSYNC")
            Dim subject = $"{code} is your LENSYNC password reset code"
            Dim text =
                $"Hello {user.FullName}," & vbCrLf & vbCrLf &
                $"Your LENSYNC password reset code is: {code}" & vbCrLf & vbCrLf &
                $"It expires in {Passwords.ResetCodeMinutes} minutes and can be used once." & vbCrLf &
                "If you did not ask to reset your password, ignore this email; your password stays the same." & vbCrLf & vbCrLf &
                studio
            Dim html =
                "<div style=""font-family:Segoe UI,Arial,sans-serif;color:#111111;max-width:480px"">" &
                $"<p style=""letter-spacing:3px;font-size:11px;color:#6B6B6B"">{Net.WebUtility.HtmlEncode(studio.ToUpperInvariant())}</p>" &
                $"<p>Hello {Net.WebUtility.HtmlEncode(user.FullName)},</p>" &
                "<p>Your LENSYNC password reset code is:</p>" &
                $"<p style=""font-size:30px;font-weight:600;letter-spacing:10px;border:2px solid #111111;display:inline-block;padding:10px 18px"">{code}</p>" &
                $"<p>It expires in {Passwords.ResetCodeMinutes} minutes and can be used once.</p>" &
                "<p style=""color:#6B6B6B;font-size:12px"">If you did not ask to reset your password, ignore this email; your password stays the same.</p>" &
                "</div>"
            Mailer.Send(user.Email, subject, text, html)
        End Sub

        ''' <summary>Step 2: check the code (max 5 tries, 10-minute life).</summary>
        Public Shared Function VerifyResetCode(userId As Integer, code As String) As ResetVerifyResult
            Dim reset = PasswordResetRepository.GetLatestUnused(userId)
            If reset Is Nothing OrElse reset.IsExpired Then
                Return New ResetVerifyResult With {.Message = "This code has expired or was replaced. Please request a new code."}
            End If
            code = If(code, "").Trim()
            If code.Length = 6 AndAlso code.All(AddressOf Char.IsDigit) AndAlso Passwords.Verify(code, reset.CodeHash) Then
                Return New ResetVerifyResult With {.Ok = True, .ResetId = reset.Id}
            End If
            Dim attempts = PasswordResetRepository.RegisterWrongCode(reset.Id)
            AuditLog.Write(AuditActions.PasswordResetCodeFailed, "users", userId, $"Wrong reset code (attempt {attempts})", userId)
            Dim left = Passwords.ResetMaxAttempts - attempts
            If left <= 0 Then
                Return New ResetVerifyResult With {.Message = "Too many wrong codes. This code no longer works; please request a new one."}
            End If
            Return New ResetVerifyResult With {.Message = $"That code is not correct. {left} {If(left = 1, "try", "tries")} left."}
        End Function

        ''' <summary>Step 3: set the new password and use up the code, in one transaction.</summary>
        Public Shared Function CompleteReset(userId As Integer, resetId As Integer, newPassword As String) As String
            Dim user = UserRepository.GetById(userId)
            If user Is Nothing OrElse Not user.IsActive Then Return "This account is no longer active."
            Dim problem = Passwords.Problem(newPassword, user.Username)
            If problem IsNot Nothing Then Return problem

            Dim newHash = Passwords.Hash(newPassword)
            Dim ok = Db.InTransaction(Of Boolean)(
                Function(conn, tx)
                    If Not PasswordResetRepository.MarkUsed(conn, tx, resetId) Then Return False
                    UserRepository.UpdatePassword(conn, tx, userId, newHash)
                    AuditLog.Write(conn, tx, AuditActions.PasswordReset, "users", userId, "Password reset with emailed code", userId)
                    Return True
                End Function)
            Return If(ok, Nothing, "This code was already used. Please request a new code.")
        End Function

    End Class

End Namespace
