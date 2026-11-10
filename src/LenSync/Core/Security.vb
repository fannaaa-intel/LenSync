Option Strict On
Option Explicit On
Option Infer On

Imports System.Security.Cryptography

Namespace Core

    ''' <summary>One password rule, shown live in the password checklist and enforced on save.</summary>
    Public NotInheritable Class PasswordRule
        Public ReadOnly Property Label As String
        Public ReadOnly Property Test As Func(Of String, Boolean)

        Public Sub New(label As String, test As Func(Of String, Boolean))
            Me.Label = label
            Me.Test = test
        End Sub
    End Class

    ''' <summary>
    ''' Security policy and primitives: BCrypt hashing, password rules, lockout limits, reset codes.
    ''' (Named Passwords, not Security, to avoid clashing with the System.Security namespace.)
    ''' </summary>
    Public NotInheritable Class Passwords

        Private Sub New()
        End Sub

        ' ---------------------------------------------------------------- policy
        Public Const MaxFailedAttempts As Integer = 5
        Public Const LockMinutes As Integer = 5
        Public Const ResetCodeMinutes As Integer = 10
        Public Const ResetMaxAttempts As Integer = 5
        Public Const ResetResendSeconds As Integer = 60
        Public Const MinLength As Integer = 8
        ''' <summary>BCrypt only uses the first 72 bytes; keep well under that.</summary>
        Public Const MaxLength As Integer = 64

        Private Const WorkFactor As Integer = 11
        Private Const CodeWorkFactor As Integer = 10
        ' Any valid cost-11 hash works here (the result is discarded): verifying against it makes unknown usernames
        ' take the same time as real ones, so response time doesn't reveal which usernames exist.
        Private Const TimingDummyHash As String = "$2a$11$ORIDLNymf8RaXS.d1rZ2k.WpPIHV6Ih2xp1QJGc.ZMsncFBH/kJsO"

        Public Shared ReadOnly Rules As IReadOnlyList(Of PasswordRule) = New List(Of PasswordRule) From {
            New PasswordRule($"At least {MinLength} characters", Function(p) p.Length >= MinLength),
            New PasswordRule("An uppercase and a lowercase letter", Function(p) p.Any(AddressOf Char.IsUpper) AndAlso p.Any(AddressOf Char.IsLower)),
            New PasswordRule("At least one number", Function(p) p.Any(AddressOf Char.IsDigit)),
            New PasswordRule("At least one symbol (e.g. ! @ # $)", Function(p) p.Any(Function(c) Not Char.IsLetterOrDigit(c) AndAlso Not Char.IsWhiteSpace(c)))
        }

        ' ---------------------------------------------------------------- hashing
        Public Shared Function Hash(password As String) As String
            Return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor)
        End Function

        Public Shared Function Verify(password As String, storedHash As String) As Boolean
            If String.IsNullOrEmpty(password) OrElse String.IsNullOrEmpty(storedHash) Then Return False
            Try
                Return BCrypt.Net.BCrypt.Verify(password, storedHash)
            Catch ex As BCrypt.Net.SaltParseException
                AppLog.Write(ex, "Stored password hash is malformed")
                Return False
            End Try
        End Function

        ''' <summary>Burns the same time as a real check, for usernames that do not exist.</summary>
        Public Shared Sub DummyVerify(password As String)
            Verify(If(password, "") & "x", TimingDummyHash)
        End Sub

        ' ---------------------------------------------------------------- rules
        ''' <summary>Returns a user-facing problem with <paramref name="password"/>, or Nothing if it is acceptable.</summary>
        Public Shared Function Problem(password As String, username As String) As String
            password = If(password, "")
            If password.Length > MaxLength Then Return $"Use at most {MaxLength} characters."
            For Each rule In Rules
                If Not rule.Test(password) Then Return "The new password needs: " & rule.Label.ToLowerInvariant() & "."
            Next
            If Not String.IsNullOrWhiteSpace(username) AndAlso
               password.IndexOf(username.Trim(), StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return "The password must not contain the username."
            End If
            Return Nothing
        End Function

        ' ---------------------------------------------------------------- reset codes
        ''' <summary>Uniform random 6-digit code from a cryptographic RNG.</summary>
        Public Shared Function NewResetCode() As String
            Using rng = RandomNumberGenerator.Create()
                Dim bytes(3) As Byte
                Dim value As UInteger
                ' Reject values above the largest multiple of 1,000,000 so every code is equally likely.
                Const limit As UInteger = 4294000000UI
                Do
                    rng.GetBytes(bytes)
                    value = BitConverter.ToUInt32(bytes, 0)
                Loop While value >= limit
                Return (value Mod 1000000UI).ToString("000000")
            End Using
        End Function

        Public Shared Function HashResetCode(code As String) As String
            Return BCrypt.Net.BCrypt.HashPassword(code, CodeWorkFactor)
        End Function

        ''' <summary>
        ''' Readable temporary password an Admin can read out, e.g. "Kap-4827-mX": meets every rule, avoids look-alike
        ''' characters (O/0, I/l/1), and never contains <paramref name="username"/>.
        ''' </summary>
        Public Shared Function NewTemporaryPassword(username As String) As String
            Const upper As String = "ABCDEFGHJKLMNPQRSTUVWXYZ"
            Const lower As String = "abcdefghijkmnpqrstuvwxyz"
            Const digits As String = "23456789"
            Using rng = RandomNumberGenerator.Create()
                Dim pick = Function(chars As String) As Char
                               Dim b(3) As Byte
                               rng.GetBytes(b)
                               Return chars(CInt(BitConverter.ToUInt32(b, 0) Mod CUInt(chars.Length)))
                           End Function
                Do
                    Dim pw = String.Concat(pick(upper), pick(lower), pick(lower), "-",
                                           pick(digits), pick(digits), pick(digits), pick(digits), "-",
                                           pick(lower), pick(upper))
                    If Problem(pw, username) Is Nothing Then Return pw
                Loop
            End Using
        End Function

        ''' <summary>Base64 random secret (e.g. report_secret).</summary>
        Public Shared Function NewSecret(byteCount As Integer) As String
            Dim bytes(byteCount - 1) As Byte
            Using rng = RandomNumberGenerator.Create()
                rng.GetBytes(bytes)
            End Using
            Return Convert.ToBase64String(bytes)
        End Function

    End Class

End Namespace
