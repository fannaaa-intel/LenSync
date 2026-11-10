Option Strict On
Option Explicit On
Option Infer On

Imports LenSync.Models

Namespace Core

    ''' <summary>The signed-in user and their permissions. Filled by Auth.SignIn, cleared by Auth.SignOut.</summary>
    Public NotInheritable Class Session

        Private Shared _user As User
        Private Shared ReadOnly _permissions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Private Shared _signedInAt As DateTime

        Private Sub New()
        End Sub

        Public Shared ReadOnly Property CurrentUser As User
            Get
                Return _user
            End Get
        End Property

        Public Shared ReadOnly Property IsSignedIn As Boolean
            Get
                Return _user IsNot Nothing
            End Get
        End Property

        ''' <summary>The logged-in user's id, or 0 when nobody is logged in (safe for UI code that may outlive the session).</summary>
        Public Shared ReadOnly Property CurrentUserId As Integer
            Get
                Return If(_user Is Nothing, 0, _user.Id)
            End Get
        End Property

        Public Shared ReadOnly Property SignedInAt As DateTime
            Get
                Return _signedInAt
            End Get
        End Property

        ''' <summary>
        ''' True if the signed-in user's role grants <paramref name="key"/> (see Permissions).
        ''' Check it in the UI (to hide things) AND again inside every action.
        ''' </summary>
        Public Shared Function HasPermission(key As String) As Boolean
            Return _user IsNot Nothing AndAlso _permissions.Contains(key)
        End Function

        ''' <summary>
        ''' Guard for actions (not just screens): throws UnauthorizedAccessException if the user lacks
        ''' <paramref name="key"/>. UI boundaries turn it into a friendly message via Db.FriendlyMessage.
        ''' </summary>
        Public Shared Sub Demand(key As String)
            If Not HasPermission(key) Then
                AuditLog.Write(AuditActions.AccessDenied, "permission", Nothing, key)
                Throw New UnauthorizedAccessException($"Permission {key} is required.")
            End If
        End Sub

        Friend Shared Sub Start(user As User, permissionKeys As IEnumerable(Of String))
            _user = user
            _permissions.Clear()
            For Each k In permissionKeys
                _permissions.Add(k)
            Next
            _signedInAt = DateTime.Now
        End Sub

        Friend Shared Sub Clear()
            _user = Nothing
            _permissions.Clear()
        End Sub

    End Class

End Namespace
