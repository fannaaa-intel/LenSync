Option Strict On
Option Explicit On
Option Infer On

Imports System.Globalization

Namespace Modules.AddService

    ''' <summary>Short, scannable dates and times for the Add Service lists.</summary>
    Friend NotInheritable Class Display

        Private Sub New()
        End Sub

        Public Shared ReadOnly PH As CultureInfo = CultureInfo.GetCultureInfo("en-PH")

        ''' <summary>"Today, 9:30 AM", "Tomorrow, 1:00 PM", "Thu, Nov 12 · 9:30 AM", "Nov 12, 2025 · 9:30 AM".</summary>
        Public Shared Function Schedule(d As DateTime) As String
            Dim time = d.ToString("h:mm tt", PH)
            Dim day = d.Date
            If day = DateTime.Today Then Return "Today, " & time
            If day = DateTime.Today.AddDays(1) Then Return "Tomorrow, " & time
            If day = DateTime.Today.AddDays(-1) Then Return "Yesterday, " & time
            If d.Year = DateTime.Today.Year Then Return d.ToString("ddd, MMM d", PH) & " · " & time
            Return d.ToString("MMM d, yyyy", PH) & " · " & time
        End Function

        Public Shared Function Stamp(d As DateTime) As String
            If d.Date = DateTime.Today Then Return "Today, " & d.ToString("h:mm tt", PH)
            Return d.ToString(If(d.Year = DateTime.Today.Year, "MMM d, h:mm tt", "MMM d, yyyy"), PH)
        End Function

        Public Shared Function Time(t As TimeSpan) As String
            Return DateTime.Today.Add(t).ToString("h:mm tt", PH)
        End Function

        ''' <summary>True if <paramref name="text"/> contains <paramref name="query"/> (case-insensitive).</summary>
        Public Shared Function Has(text As String, query As String) As Boolean
            Return If(text, "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
        End Function

    End Class

End Namespace
