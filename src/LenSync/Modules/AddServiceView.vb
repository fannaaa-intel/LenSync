Option Strict On
Option Explicit On
Option Infer On

Imports LenSync.Modules.AddService

Namespace Modules

    ''' <summary>Add Service module: Bookings (schedule + downpayment), Billing (bill and take payment), Sales (receipts).</summary>
    Public Class AddServiceView
        Inherits TabbedView

        Public Sub New()
            MyBase.New({"Bookings", "Billing", "Sales"})
        End Sub

        Protected Overrides Function CreatePage(index As Integer) As ModulePage
            Select Case index
                Case 0 : Return New BookingsPage()
                Case 1 : Return New BillingPage()
                Case Else : Return New SalesPage()
            End Select
        End Function

    End Class

End Namespace
