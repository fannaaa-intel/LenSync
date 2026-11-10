Option Strict On
Option Explicit On
Option Infer On

Imports LenSync.Data
Imports LenSync.Models

Namespace Core

    ''' <summary>Dashboard data: today's sales and shoots, the last 7 days, recent receipts, bookings waiting to be billed.</summary>
    Public NotInheritable Class DashboardService

        Private Sub New()
        End Sub

        Public Const RecentCount As Integer = 6

        Public Shared Function Load() As DashboardSnapshot
            Session.Demand(Permissions.Dashboard)
            Dim today = DateTime.Today
            Return New DashboardSnapshot With {
                .Week = DashboardRepository.SalesByDay(today.AddDays(-6), today),
                .VoidedToday = DashboardRepository.VoidedOn(today),
                .ToBill = DashboardRepository.CountToBill(),
                .Schedule = BookingRepository.ListForDay(today),
                .Recent = TransactionRepository.List(Nothing, RecentCount),
                .LoadedAt = DateTime.Now}
        End Function

    End Class

End Namespace
