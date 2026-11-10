Option Strict On
Option Explicit On
Option Infer On

Namespace Models

    Public Class Role
        Public Property Id As Integer
        Public Property Name As String = ""
        Public Property Description As String = ""

        ''' <summary>The Admin role always keeps every permission (so nobody can lock the studio out of Settings).</summary>
        Public ReadOnly Property IsAdmin As Boolean
            Get
                Return String.Equals(Name, Role.AdminName, StringComparison.OrdinalIgnoreCase)
            End Get
        End Property

        Public Const AdminName As String = "Admin"
    End Class

    Public Class Permission
        Public Property Id As Integer
        Public Property Key As String = ""
        Public Property Label As String = ""
    End Class

    ''' <summary>A row in the Settings > Users list.</summary>
    Public Class UserSummary
        Public Property Id As Integer
        Public Property Username As String = ""
        Public Property FullName As String = ""
        Public Property Email As String = ""
        Public Property RoleId As Integer
        Public Property RoleName As String = ""
        Public Property IsActive As Boolean
        Public Property IsLocked As Boolean
        Public Property MustChangePassword As Boolean
        Public Property LastLogin As DateTime?
    End Class

    ''' <summary>A package (name, inclusions, price) or an add-on (name, price).</summary>
    Public Class PriceItem
        Public Property Id As Integer
        Public Property Name As String = ""
        Public Property Inclusions As String = ""
        Public Property Price As Decimal
        Public Property IsActive As Boolean
    End Class

    Public Enum PriceItemKind
        Package
        Addon
    End Enum

    ''' <summary>
    ''' Outcome of an admin action. On failure, Errors maps a field key (e.g. "username") to a user-facing message
    ''' so the dialog can show each one inline; an empty key means a form-level message.
    ''' </summary>
    Public Class AdminResult
        Public ReadOnly Property Errors As New List(Of KeyValuePair(Of String, String))
        Public Property Value As Object

        Public ReadOnly Property Ok As Boolean
            Get
                Return Errors.Count = 0
            End Get
        End Property

        Public Sub AddError(field As String, message As String)
            Errors.Add(New KeyValuePair(Of String, String)(If(field, ""), message))
        End Sub

        Public Shared Function Fail(field As String, message As String) As AdminResult
            Dim r As New AdminResult()
            r.AddError(field, message)
            Return r
        End Function

        Public Shared Function Success(Optional value As Object = Nothing) As AdminResult
            Return New AdminResult With {.Value = value}
        End Function
    End Class

End Namespace
