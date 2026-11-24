Option Strict On
Option Explicit On
Option Infer On

Imports LenSync.Data
Imports LenSync.Models

Namespace Core

    ''' <summary>
    ''' Dashboard data: today's revenue and shoots, bookings awaiting payment, recent receipts, and the sales chart
    ''' (Daily = last 7 days, Weekly = last 8 weeks starting Monday, Monthly = last 6 months).
    ''' </summary>
    Public NotInheritable Class DashboardService

        Private Sub New()
        End Sub

        Public Const RecentCount As Integer = 6

        ''' <summary>Number of bars for each period.</summary>
        Public Shared Function BucketCount(period As SalesPeriod) As Integer
            Select Case period
                Case SalesPeriod.Weekly : Return 8
                Case SalesPeriod.Monthly : Return 6
                Case Else : Return 7
            End Select
        End Function

        ''' <summary>The empty bars (oldest first) for <paramref name="period"/>; the last one contains <paramref name="today"/>.</summary>
        Public Shared Function EmptyBuckets(period As SalesPeriod, today As Date) As List(Of SalesBucket)
            Dim t = today.Date
            Dim n = BucketCount(period)
            Dim result As New List(Of SalesBucket)
            Select Case period
                Case SalesPeriod.Weekly
                    Dim monday = t.AddDays(-((CInt(t.DayOfWeek) + 6) Mod 7))
                    For i = n - 1 To 0 Step -1
                        Dim first = monday.AddDays(-7 * i)
                        result.Add(New SalesBucket With {.First = first, .Last = first.AddDays(6)})
                    Next
                Case SalesPeriod.Monthly
                    Dim monthStart = New Date(t.Year, t.Month, 1)
                    For i = n - 1 To 0 Step -1
                        Dim first = monthStart.AddMonths(-i)
                        result.Add(New SalesBucket With {.First = first, .Last = first.AddMonths(1).AddDays(-1)})
                    Next
                Case Else
                    For i = n - 1 To 0 Step -1
                        Dim d = t.AddDays(-i)
                        result.Add(New SalesBucket With {.First = d, .Last = d})
                    Next
            End Select
            Return result
        End Function

        Public Shared Function Load(period As SalesPeriod) As DashboardSnapshot
            Session.Demand(Permissions.Dashboard)
            Dim today = DateTime.Today
            Dim buckets = EmptyBuckets(period, today)
            ' One daily query covers the chart and the today/yesterday tile (the current week/month may end after today).
            Dim firstDay = If(buckets(0).First < today.AddDays(-1), buckets(0).First, today.AddDays(-1))
            Dim days = DashboardRepository.SalesByDay(firstDay, today)
            For Each b In buckets
                For Each d In days.Where(Function(x) b.Contains(x.First))
                    b.Total += d.Total
                    b.Receipts += d.Receipts
                Next
            Next
            Return New DashboardSnapshot With {
                .Period = period,
                .Buckets = buckets,
                .Today = days.Last(),
                .Yesterday = days(days.Count - 2),
                .AwaitingPayment = DashboardRepository.CountAwaitingPayment(),
                .Schedule = BookingRepository.ListForDay(today),
                .Recent = TransactionRepository.List(Nothing, RecentCount),
                .LoadedAt = DateTime.Now}
        End Function

    End Class

End Namespace
