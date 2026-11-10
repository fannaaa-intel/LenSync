Option Strict On
Option Explicit On
Option Infer On

Namespace Core

    ''' <summary>Module permission keys (permissions.perm_key). Roles get them via role_permissions.</summary>
    Public NotInheritable Class Permissions

        Private Sub New()
        End Sub

        Public Const Dashboard As String = "DASHBOARD"
        ''' <summary>Add Service: bookings (create, downpayment, schedule) and billing/payment in one module.</summary>
        Public Const AddService As String = "ADD_SERVICE"
        Public Const Reports As String = "REPORTS"
        Public Const Settings As String = "SETTINGS"
        Public Const VoidSale As String = "VOID_SALE"

    End Class

End Namespace
