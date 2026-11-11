Option Strict On
Option Explicit On
Option Infer On

Imports System.Globalization
Imports LenSync.Core
Imports LenSync.Models

Namespace UI.Controls

    ''' <summary>
    ''' Reports > Verify result: a card whose viewfinder brackets focus in and whose lens icon draws itself when a
    ''' result arrives. Genuine = check icon + heavy ink brackets + the report's record + whether the period's data
    ''' is unchanged since printing; code mismatch / not found = cross / alert icon and a short shake. Meaning is carried
    ''' by icon, heading text and weight, never color.
    ''' </summary>
    Public Class VerifyResultCard
        Inherits Control

        Private Shared ReadOnly PH As CultureInfo = CultureInfo.GetCultureInfo("en-PH")
        Private _result As VerifyResult
        Private _reportNo As String = ""
        Private _t As Double = 1
        Private _shakeX As Integer
        Private ReadOnly _tween As New Tween()
        Private ReadOnly _shake As New Tween()

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.ResizeRedraw, True)
            SetStyle(ControlStyles.Selectable, False)
            TabStop = False
            BackColor = Theme.White
            AccessibleRole = AccessibleRole.StaticText
            UpdateAccessibleText()
        End Sub

        ''' <summary>The result to show (Nothing = the "how to verify" state). <paramref name="reportNo"/> is what was typed.</summary>
        Public Sub ShowResult(result As VerifyResult, reportNo As String, Optional animate As Boolean = True)
            _result = result
            _reportNo = If(reportNo, "")
            UpdateAccessibleText()
            _shake.Stop()
            _shakeX = 0
            If Not animate OrElse Not IsHandleCreated Then
                _t = 1
                Invalidate()
                Return
            End If
            Dim bad = result IsNot Nothing AndAlso result.Outcome <> VerifyOutcome.Genuine
            _tween.Start(Theme.OpenMs + 120,
                Sub(t)
                    _t = t
                    Invalidate()
                End Sub,
                Sub()
                    If bad Then StartShake()
                End Sub)
        End Sub

        Public ReadOnly Property Result As VerifyResult
            Get
                Return _result
            End Get
        End Property

        Private Sub StartShake()
            Dim amplitude = Theme.Dpi(6)
            _shake.Start(Theme.ShakeMs,
                Sub(t)
                    _shakeX = CInt(amplitude * Easing.Shake(t))
                    Invalidate()
                End Sub,
                Sub()
                    _shakeX = 0
                    Invalidate()
                End Sub)
        End Sub

        ' ---------------------------------------------------------------- texts
        Private Function Heading() As String
            If _result Is Nothing Then Return "Check a printed report"
            Select Case _result.Outcome
                Case VerifyOutcome.Genuine : Return "Genuine report"
                Case VerifyOutcome.CodeMismatch : Return "Code doesn't match"
                Case Else : Return "Report not found"
            End Select
        End Function

        Private Function Subtitle() As String
            If _result Is Nothing Then
                Return "Type the report no. and the verification code printed at the bottom of the paper report. " &
                       "LENSYNC checks the code and whether the sales behind the report changed after it was printed."
            End If
            Select Case _result.Outcome
                Case VerifyOutcome.Genuine
                    Return $"{_result.Log.ReportNo} was printed from this LENSYNC system and its code is correct."
                Case VerifyOutcome.CodeMismatch
                    Return $"{_reportNo} exists, but this is not the code printed on it. Check for a typing mistake. " &
                           "If the code was typed correctly, the paper may have been altered: treat it as not genuine."
                Case Else
                    Return $"No report numbered {_reportNo} was printed from this LENSYNC system. Check the number, or treat the paper as not genuine."
            End Select
        End Function

        Private Function Icon() As IconKind
            If _result Is Nothing Then Return IconKind.Info
            Select Case _result.Outcome
                Case VerifyOutcome.Genuine : Return IconKind.Check
                Case VerifyOutcome.CodeMismatch : Return IconKind.Cross
                Case Else : Return IconKind.Alert
            End Select
        End Function

        Private Function Details() As List(Of KeyValuePair(Of String, String))
            Dim rows As New List(Of KeyValuePair(Of String, String))
            If _result Is Nothing OrElse _result.Outcome <> VerifyOutcome.Genuine OrElse _result.Log Is Nothing Then Return rows
            Dim log = _result.Log
            rows.Add(New KeyValuePair(Of String, String)("Report", log.TypeTitle))
            rows.Add(New KeyValuePair(Of String, String)("Period", ReportService.PeriodText(log.DateFrom, log.DateTo)))
            rows.Add(New KeyValuePair(Of String, String)("Net sales", Money.Format(log.TotalAmount)))
            rows.Add(New KeyValuePair(Of String, String)("Printed by", log.GeneratedBy))
            Dim printer = If(String.IsNullOrEmpty(log.PrinterName), "", "  ·  " & log.PrinterName)
            rows.Add(New KeyValuePair(Of String, String)("Printed", log.GeneratedAt.ToString("MMM d, yyyy h:mm tt", PH) & printer))
            Return rows
        End Function

        Private Sub UpdateAccessibleText()
            Dim text = Heading() & ". " & Subtitle()
            If _result IsNot Nothing AndAlso _result.Outcome = VerifyOutcome.Genuine Then
                text &= " " & If(_result.DataUnchanged, "Data unchanged since printing.", "Data changed since printing.")
            End If
            AccessibleName = Heading()
            AccessibleDescription = text
        End Sub

        ' ---------------------------------------------------------------- paint
        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(Theme.White)
            Gfx.HighQuality(g)
            Dim ease = Easing.OutCubic(_t)
            Dim genuine = _result IsNot Nothing AndAlso _result.Outcome = VerifyOutcome.Genuine
            Dim idle = _result Is Nothing
            Dim inset = Theme.Dpi(2)
            Dim frame As New RectangleF(inset + _shakeX, inset, Width - inset * 2 - 1, Height - inset * 2 - 1)

            ' Card: snow fill for results, brackets "focus" in. Heavier ink brackets for a genuine report.
            Using path = Gfx.RoundedRect(frame, Theme.Dpi(CSng(Theme.Radius)))
                Using b As New SolidBrush(If(idle, Theme.White, Theme.Blend(Theme.White, Theme.Snow, ease)))
                    g.FillPath(b, path)
                End Using
                Using p As New Pen(Theme.Mist)
                    g.DrawPath(p, path)
                End Using
            End Using
            Dim bracketColor = If(idle, Theme.Silver, If(genuine, Theme.Ink, Theme.Slate))
            Using p As New Pen(bracketColor, Theme.Dpi(If(genuine, 2.5F, 1.5F)))
                Dim vf = RectangleF.Inflate(frame, -Theme.Dpi(8.0F), -Theme.Dpi(8.0F))
                Gfx.DrawViewfinder(g, vf, Theme.Dpi(22.0F), p, If(idle, 1, 0.25 + 0.75 * ease))
            End Using

            Dim pad = Theme.Dpi(30)
            Dim lift = CInt(Theme.Dpi(10) * (1 - ease))
            Dim x = CInt(frame.X) + pad
            Dim y = pad + lift
            Dim innerW = CInt(frame.Width) - pad * 2
            Dim fade = Function(c As Color) Theme.Blend(Theme.Snow, c, ease)

            ' Icon + heading + subtitle.
            Dim iconD = Theme.Dpi(44.0F)
            Gfx.DrawLensIcon(g, New PointF(x + iconD / 2, y + iconD / 2), iconD, Icon(), If(idle, 1, ease), If(idle, 1, Easing.Clamp(_t * 1.6 - 0.5)),
                             If(idle, Theme.Silver, Theme.Ink), Theme.Dpi(If(genuine, 2.2F, 1.8F)))
            Dim tx = x + CInt(iconD) + Theme.Dpi(16)
            Dim tw = innerW - (tx - x)
            Dim headFont = If(idle, Theme.BodyBold, Theme.Title)
            Const single1 As TextFormatFlags = TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine Or TextFormatFlags.EndEllipsis
            Const wrap As TextFormatFlags = TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding Or TextFormatFlags.WordBreak Or TextFormatFlags.EndEllipsis Or TextFormatFlags.TextBoxControl
            TextRenderer.DrawText(g, Heading(), headFont, New Rectangle(tx, y, tw, headFont.Height), fade(Theme.Ink), single1)
            Dim subTop = y + headFont.Height + Theme.Dpi(4)
            Dim subH = TextRenderer.MeasureText(g, Subtitle(), Theme.Label, New Size(tw, Integer.MaxValue), wrap).Height
            TextRenderer.DrawText(g, Subtitle(), Theme.Label, New Rectangle(tx, subTop, tw, subH + Theme.Dpi(2)), fade(Theme.Slate), wrap)
            y = Math.Max(y + CInt(iconD), subTop + subH) + Theme.Dpi(18)
            If Not genuine Then Return

            ' The report's record.
            Using p As New Pen(Theme.Mist)
                g.DrawLine(p, x, y, x + innerW, y)
            End Using
            y += Theme.Dpi(12)
            Dim keyW = Theme.Dpi(96)
            Dim rowH = Theme.Body.Height + Theme.Dpi(8)
            For Each kv In Details()
                TextRenderer.DrawText(g, kv.Key, Theme.Label, New Rectangle(tx, y, keyW, rowH), fade(Theme.Slate), single1 Or TextFormatFlags.VerticalCenter)
                TextRenderer.DrawText(g, kv.Value, Theme.Body, New Rectangle(tx + keyW, y, tw - keyW, rowH), fade(Theme.Ink), single1 Or TextFormatFlags.VerticalCenter)
                y += rowH
            Next
            y += Theme.Dpi(12)

            ' Data still the same as when printed? A strip like InlineNotice: heavier when it changed.
            Dim changed = Not _result.DataUnchanged
            Dim stripH = Math.Max(Theme.Dpi(58), Height - y - pad)
            stripH = Math.Min(stripH, Theme.Dpi(72))
            Dim strip As New Rectangle(x, y, innerW, stripH)
            Using path = Gfx.RoundedRect(strip, Theme.Dpi(CSng(Theme.RadiusSmall)))
                Using b As New SolidBrush(Theme.White)
                    g.FillPath(b, path)
                End Using
                Using p As New Pen(Theme.Mist)
                    g.DrawPath(p, path)
                End Using
            End Using
            Dim barW = Theme.Dpi(If(changed, 4, 2))
            Dim barH = CInt((stripH - Theme.Dpi(16)) * ease)
            Using b As New SolidBrush(Theme.Ink)
                g.FillRectangle(b, strip.X, strip.Y + (stripH - barH) \ 2, barW, barH)
            End Using
            Dim smallD = Theme.Dpi(20.0F)
            Gfx.DrawLensIcon(g, New PointF(strip.X + Theme.Dpi(18) + smallD / 2, strip.Y + stripH / 2.0F), smallD,
                             If(changed, IconKind.Alert, IconKind.Check), ease, Easing.Clamp(_t * 1.6 - 0.6), Theme.Ink, Theme.Dpi(1.5F))
            Dim sx = strip.X + Theme.Dpi(18) + CInt(smallD) + Theme.Dpi(12)
            Dim sw = strip.Right - sx - Theme.Dpi(12)
            Dim title = If(changed, "Data changed since printing", "Data unchanged since printing")
            Dim note = If(changed,
                          "Sales in this period were added, voided or edited after printing. The paper is genuine but out of date: print a new report for current figures.",
                          "The sales records for this period still give the same figures as the paper.")
            Dim titleFont = If(changed, Theme.BodyBold, Theme.LabelBold)
            Dim noteH = TextRenderer.MeasureText(g, note, Theme.Hint, New Size(sw, Integer.MaxValue), wrap).Height
            Dim blockH = titleFont.Height + Theme.Dpi(2) + noteH
            Dim by = strip.Y + Math.Max(Theme.Dpi(6), (stripH - blockH) \ 2)
            TextRenderer.DrawText(g, title, titleFont, New Rectangle(sx, by, sw, titleFont.Height), fade(Theme.Ink), single1)
            TextRenderer.DrawText(g, note, Theme.Hint, New Rectangle(sx, by + titleFont.Height + Theme.Dpi(2), sw, Math.Min(noteH, strip.Bottom - by - titleFont.Height - Theme.Dpi(4))),
                                  fade(Theme.Charcoal), wrap)
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                _tween.Dispose()
                _shake.Dispose()
            End If
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Namespace
