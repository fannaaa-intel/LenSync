Option Strict On
Option Explicit On
Option Infer On

Namespace Models

    ''' <summary>How the Dashboard sales chart groups sales: one bar per day, week (Mon-Sun) or month.</summary>
    Public Enum SalesPeriod
        Daily
        Weekly
        Monthly
    End Enum

    ''' <summary>Completed sales from <see cref="First"/> through <see cref="Last"/> (voided sales are never counted).</summary>
    Public Class SalesBucket
        Public Property First As Date
        Public Property Last As Date
        Public Property Total As Decimal
        Public Property Receipts As Integer

        Public Function Contains(day As Date) As Boolean
            Return day.Date >= First AndAlso day.Date <= Last
        End Function
    End Class

    ''' <summary>Everything the Dashboard shows, loaded in one go.</summary>
    Public Class DashboardSnapshot
        Public Property Period As SalesPeriod
        ''' <summary>The chart's bars, oldest first; the last one contains today.</summary>
        Public Property Buckets As New List(Of SalesBucket)
        Public Property Today As New SalesBucket()
        Public Property Yesterday As New SalesBucket()
        Public Property AwaitingPayment As Integer
        Public Property Schedule As New List(Of Booking)
        Public Property Recent As New List(Of SaleSummary)
        Public Property LoadedAt As DateTime

        ''' <summary>Today's shoots that haven't been marked done yet.</summary>
        Public ReadOnly Property ShootsPending As Integer
            Get
                Return Schedule.Where(Function(b) b.Status = BookingStatus.Pending).Count()
            End Get
        End Property
    End Class

End Namespace
