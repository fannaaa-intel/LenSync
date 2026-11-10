Option Strict On
Option Explicit On
Option Infer On

Namespace Models

    ''' <summary>A LENSYNC account. PasswordHash is Friend so it never reaches UI binding or logs.</summary>
    Public Class User
        Public Property Id As Integer
        Public Property Username As String = ""
        Public Property FullName As String = ""
        Public Property Email As String
        Public Property RoleId As Integer
        Public Property RoleName As String = ""
        Public Property IsActive As Boolean
        Public Property MustChangePassword As Boolean
        Public Property FailedAttempts As Integer
        Public Property LastLogin As DateTime?

        ''' <summary>locked_until is in the future (evaluated with the database clock).</summary>
        Public Property IsLocked As Boolean
        ''' <summary>locked_until is set but already passed; the counter should restart.</summary>
        Public Property LockExpired As Boolean
        Public Property LockSecondsLeft As Integer

        Friend Property PasswordHash As String = ""
    End Class

    ''' <summary>One emailed password-reset code (only its hash is stored).</summary>
    Public Class PasswordReset
        Public Property Id As Integer
        Public Property UserId As Integer
        Public Property Attempts As Integer
        Public Property IsExpired As Boolean
        Friend Property CodeHash As String = ""
    End Class

End Namespace
