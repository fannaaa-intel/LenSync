Option Strict On
Option Explicit On
Option Infer On

Namespace Models

    ''' <summary>Completed sales on one day (voided sales are never counted).</summary>
    Public Class DaySales
        Public Property Day As Date
        Public Property Total As Decimal
        Public Property Receipts As Integer
    End Class

    ''' <summary>Everything the Dashboard shows, loaded in one go.</summary>
    Public Class DashboardSnapshot
        ''' <summary>The last 7 days, oldest first; the last entry is today.</summary>
        Public Property Week As New List(Of DaySales)
        Public Property VoidedToday As Integer
        Public Property ToBill As Integer
        Public Property Schedule As New List(Of Booking)
        Public Property Recent As New List(Of SaleSummary)
        Public Property LoadedAt As DateTime

        Public ReadOnly Property Today As DaySales
            Get
                Return Week(Week.Count - 1)
            End Get
        End Property

        Public ReadOnly Property Yesterday As DaySales
            Get
                Return Week(Week.Count - 2)
            End Get
        End Property

        ''' <summary>Today's shoots that haven't been marked done yet.</summary>
        Public ReadOnly Property ShootsPending As Integer
            Get
                Return Schedule.Where(Function(b) b.Status = BookingStatus.Pending).Count()
            End Get
        End Property
    End Class

End Namespace
