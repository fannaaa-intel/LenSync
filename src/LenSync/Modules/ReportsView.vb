Option Strict On
Option Explicit On
Option Infer On

Imports LenSync.Models
Imports LenSync.Modules.Reports

Namespace Modules

    ''' <summary>Reports module: Create report (preview + print with proof), History (printed reports), Verify (check a paper report).</summary>
    Public Class ReportsView
        Inherits TabbedView

        Private _create As CreateReportPage
        Private _pending As ReportLog

        Public Sub New()
            MyBase.New({"Create report", "History", "Verify"})
        End Sub

        Protected Overrides Function CreatePage(index As Integer) As ModulePage
            Select Case index
                Case 0
                    _create = New CreateReportPage()
                    If _pending IsNot Nothing Then ApplyPending()
                    Return _create
                Case 1 : Return New HistoryPage(AddressOf OpenAgain)
                Case Else : Return New VerifyPage()
            End Select
        End Function

        ''' <summary>History > Open again: same report and period on the Create tab (it rebuilds when shown).</summary>
        Private Sub OpenAgain(log As ReportLog)
            _pending = log
            If _create IsNot Nothing Then ApplyPending()
            SelectTab(0)
        End Sub

        Private Sub ApplyPending()
            Dim t As ReportType
            If ReportTypes.FromKey(_pending.TypeKey, t) Then _create.Apply(t, _pending.DateFrom, _pending.DateTo)
            _pending = Nothing
        End Sub

    End Class

End Namespace
