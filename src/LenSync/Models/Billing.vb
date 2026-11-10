Option Strict On
Option Explicit On
Option Infer On

Namespace Models

    ''' <summary>bookings.status values. Flow: Pending -> Unpaid (shoot done) -> Paid, or Pending/Unpaid -> Cancelled.</summary>
    Public NotInheritable Class BookingStatus
        Private Sub New()
        End Sub
        Public Const Pending As String = "Pending"
        Public Const Unpaid As String = "Unpaid"
        Public Const Paid As String = "Paid"
        Public Const Cancelled As String = "Cancelled"
    End Class

    ''' <summary>Cash, GCash, Maya. E-wallets need a reference number (no API integration).</summary>
    Public NotInheritable Class PaymentMethods
        Private Sub New()
        End Sub
        Public Const Cash As String = "Cash"
        Public Const GCash As String = "GCash"
        Public Const Maya As String = "Maya"
        Public Shared ReadOnly All As String() = {Cash, GCash, Maya}

        Public Shared Function IsEWallet(method As String) As Boolean
            Return method = GCash OrElse method = Maya
        End Function
    End Class

    ''' <summary>transaction_items.item_type values.</summary>
    Public NotInheritable Class ItemTypes
        Private Sub New()
        End Sub
        Public Const Package As String = "Package"
        Public Const Addon As String = "Addon"
        Public Const Other As String = "Other"
    End Class

    ''' <summary>transactions.discount_type values.</summary>
    Public NotInheritable Class DiscountTypes
        Private Sub New()
        End Sub
        Public Const Fixed As String = "Fixed"
        Public Const Percent As String = "Percent"
    End Class

    Public Enum BookingFilter
        ''' <summary>Pending and Unpaid (still to shoot or to bill).</summary>
        Open
        Paid
        Cancelled
        All
    End Enum

    Public Class Booking
        Public Property Id As Integer
        Public Property BookingNo As String = ""
        Public Property CustomerName As String = ""
        Public Property ContactNo As String = ""
        Public Property CustomerEmail As String = ""
        Public Property PackageId As Integer?
        Public Property PackageName As String = ""
        Public Property PackagePrice As Decimal
        Public Property ScheduleAt As DateTime
        Public Property Downpayment As Decimal
        ''' <summary>Cash / GCash / Maya, or "" when there is no downpayment.</summary>
        Public Property DownpaymentMethod As String = ""
        Public Property DownpaymentRef As String = ""
        Public Property Status As String = BookingStatus.Pending
        Public Property Notes As String = ""

        Public ReadOnly Property IsEditable As Boolean
            Get
                Return Status = BookingStatus.Pending OrElse Status = BookingStatus.Unpaid
            End Get
        End Property
    End Class

    ''' <summary>One line on a bill (and on transaction_items once saved).</summary>
    Public Class BillLine
        Public Property ItemType As String = ItemTypes.Other
        ''' <summary>packages.id or addons.id; Nothing for Other.</summary>
        Public Property RefId As Integer?
        Public Property Description As String = ""
        Public Property Qty As Integer = 1
        Public Property UnitPrice As Decimal

        Public ReadOnly Property LineTotal As Decimal
            Get
                Return Qty * UnitPrice
            End Get
        End Property

        Public Function Clone() As BillLine
            Return DirectCast(MemberwiseClone(), BillLine)
        End Function
    End Class

    ''' <summary>A discount: Fixed (peso amount) or Percent of the subtotal. A reason is always required.</summary>
    Public Class Discount
        Public Property Kind As String = DiscountTypes.Fixed
        Public Property Value As Decimal
        Public Property Reason As String = ""
    End Class

    Public Class BillTotals
        Public Property Subtotal As Decimal
        Public Property DiscountAmount As Decimal
        Public Property Total As Decimal
        Public Property Downpayment As Decimal
        Public Property BalanceDue As Decimal
    End Class

    ''' <summary>What the cashier submits when completing a payment.</summary>
    Public Class SaleRequest
        Public Property BookingId As Integer
        Public Property Lines As New List(Of BillLine)
        ''' <summary>Nothing = no discount.</summary>
        Public Property Discount As Discount
        Public Property PaymentMethod As String = PaymentMethods.Cash
        Public Property ReferenceNo As String = ""
        Public Property AmountTendered As Decimal
        ''' <summary>The balance the cashier saw; the save stops if prices changed meanwhile.</summary>
        Public Property ExpectedBalance As Decimal
    End Class

    Public Class SaleOutcome
        Public Property TransactionId As Integer
        Public Property ReceiptNo As String = ""
        Public Property ChangeAmount As Decimal
    End Class

    Public Enum SalesRange
        Today
        Last7Days
        Last30Days
        All
    End Enum

    ''' <summary>A row in Add Service > Sales.</summary>
    Public Class SaleSummary
        Public Property Id As Integer
        Public Property ReceiptNo As String = ""
        Public Property CreatedAt As DateTime
        Public Property CustomerName As String = ""
        Public Property BookingNo As String = ""
        Public Property PaymentMethod As String = ""
        Public Property Total As Decimal
        Public Property BalanceDue As Decimal
        Public Property CashierName As String = ""
        Public Property Status As String = ""
    End Class

    ''' <summary>Everything printed on the acknowledgment receipt.</summary>
    Public Class Receipt
        Public Property TransactionId As Integer
        Public Property ReceiptNo As String = ""
        Public Property CreatedAt As DateTime
        Public Property BookingNo As String = ""
        Public Property CustomerName As String = ""
        Public Property ContactNo As String = ""
        Public Property ScheduleAt As DateTime
        Public Property Lines As New List(Of BillLine)
        Public Property Subtotal As Decimal
        Public Property DiscountType As String = ""
        Public Property DiscountValue As Decimal
        Public Property DiscountAmount As Decimal
        Public Property DiscountReason As String = ""
        Public Property Total As Decimal
        Public Property Downpayment As Decimal
        Public Property DownpaymentMethod As String = ""
        Public Property DownpaymentRef As String = ""
        Public Property BalanceDue As Decimal
        Public Property PaymentMethod As String = ""
        Public Property ReferenceNo As String = ""
        Public Property AmountTendered As Decimal
        Public Property ChangeAmount As Decimal
        Public Property CashierName As String = ""
        Public Property Status As String = ""
        Public Property VoidReason As String = ""
        Public Property VoidedAt As DateTime?
        Public Property PrintedCount As Integer
        ' Studio details (from settings) at the time the receipt is shown/printed.
        Public Property BusinessName As String = ""
        Public Property Address As String = ""
        Public Property Contact As String = ""
        Public Property LogoPath As String = ""

        Public ReadOnly Property IsVoided As Boolean
            Get
                Return Status = "Voided"
            End Get
        End Property
    End Class

End Namespace
