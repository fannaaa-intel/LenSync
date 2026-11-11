Option Strict On
Option Explicit On
Option Infer On

Namespace Models

    Public Enum ReportType
        DailySales
        Transactions
        PaymentMethods
        Staff
        Packages
    End Enum

    ''' <summary>Names and keys (report_logs.report_type) for each report.</summary>
    Public NotInheritable Class ReportTypes
        Private Sub New()
        End Sub

        Public Shared ReadOnly All As ReportType() = {ReportType.DailySales, ReportType.Transactions, ReportType.PaymentMethods, ReportType.Staff, ReportType.Packages}

        Public Shared Function Title(t As ReportType) As String
            Select Case t
                Case ReportType.DailySales : Return "Daily sales"
                Case ReportType.Transactions : Return "Transactions"
                Case ReportType.PaymentMethods : Return "Sales by payment method"
                Case ReportType.Staff : Return "Sales by staff"
                Case Else : Return "Sales by package"
            End Select
        End Function

        Public Shared Function Description(t As ReportType) As String
            Select Case t
                Case ReportType.DailySales : Return "One line per day: receipts, gross amount, discounts, net sales, downpayments and balances paid."
                Case ReportType.Transactions : Return "Every receipt in the period. Voided receipts are listed but not added to the totals."
                Case ReportType.PaymentMethods : Return "Money received through Cash, GCash and Maya, counting both downpayments and balance payments."
                Case ReportType.Staff : Return "Receipts, net sales, discounts given and voided sales for each cashier."
                Case Else : Return "How often each package and add-on was billed, and the amount (before discounts)."
            End Select
        End Function

        Public Shared Function Key(t As ReportType) As String
            Select Case t
                Case ReportType.DailySales : Return "DAILY_SALES"
                Case ReportType.Transactions : Return "TRANSACTIONS"
                Case ReportType.PaymentMethods : Return "BY_PAYMENT_METHOD"
                Case ReportType.Staff : Return "BY_STAFF"
                Case Else : Return "BY_PACKAGE"
            End Select
        End Function

        Public Shared Function FromKey(key As String, ByRef t As ReportType) As Boolean
            For Each candidate In All
                If Models.ReportTypes.Key(candidate) = key Then
                    t = candidate
                    Return True
                End If
            Next
            Return False
        End Function
    End Class

    Public Class ReportColumn
        Public Property Header As String = ""
        ''' <summary>Relative width.</summary>
        Public Property Weight As Single = 1
        Public Property AlignRight As Boolean
    End Class

    ''' <summary>One table of a report (most reports have one; By package has packages and add-ons).</summary>
    Public Class ReportTable
        Public Property Title As String = ""
        Public Property Columns As New List(Of ReportColumn)
        Public Property Rows As New List(Of String())
        ''' <summary>Rows drawn grey and marked (e.g. voided receipts), parallel to Rows.</summary>
        Public Property Muted As New List(Of Boolean)
        ''' <summary>The totals line, or Nothing.</summary>
        Public Property Totals As String()
        Public Property Note As String = ""

        Public Sub AddColumn(header As String, weight As Single, Optional alignRight As Boolean = False)
            Columns.Add(New ReportColumn With {.Header = header, .Weight = weight, .AlignRight = alignRight})
        End Sub

        Public Sub AddRow(muted As Boolean, ParamArray cells As String())
            Rows.Add(cells)
            Me.Muted.Add(muted)
        End Sub
    End Class

    ''' <summary>A built report. ReportNo, code and the "generated" fields are filled when it is printed (issued).</summary>
    Public Class ReportData
        Public Property Type As ReportType
        Public Property DateFrom As Date
        Public Property DateTo As Date
        Public Property Summary As New List(Of KeyValuePair(Of String, String))
        Public Property Tables As New List(Of ReportTable)
        Public Property NetSales As Decimal

        Public Property ReportNo As String = ""
        Public Property VerificationCode As String = ""
        Public Property ContentHash As String = ""
        Public Property GeneratedBy As String = ""
        Public Property GeneratedAt As DateTime?
        Public Property PrinterName As String = ""
        ' Studio details for the page header and signature.
        Public Property BusinessName As String = ""
        Public Property Address As String = ""
        Public Property Contact As String = ""
        Public Property LogoPath As String = ""
        Public Property SignaturePath As String = ""

        Public ReadOnly Property Title As String
            Get
                Return ReportTypes.Title(Type)
            End Get
        End Property

        Public ReadOnly Property RowCount As Integer
            Get
                Return Tables.Sum(Function(t) t.Rows.Count)
            End Get
        End Property

        Public ReadOnly Property IsIssued As Boolean
            Get
                Return ReportNo.Length > 0
            End Get
        End Property
    End Class

    ''' <summary>A row of report_logs (Reports > History, Verify).</summary>
    Public Class ReportLog
        Public Property Id As Integer
        Public Property ReportNo As String = ""
        Public Property TypeKey As String = ""
        Public Property DateFrom As Date
        Public Property DateTo As Date
        Public Property RowCount As Integer
        Public Property TotalAmount As Decimal
        Public Property ContentHash As String = ""
        Public Property VerificationCode As String = ""
        Public Property GeneratedBy As String = ""
        Public Property PrinterName As String = ""
        Public Property GeneratedAt As DateTime

        Public ReadOnly Property TypeTitle As String
            Get
                Dim t As ReportType
                Return If(ReportTypes.FromKey(TypeKey, t), ReportTypes.Title(t), TypeKey)
            End Get
        End Property
    End Class

    Public Enum VerifyOutcome
        NotFound
        CodeMismatch
        Genuine
    End Enum

    Public Class VerifyResult
        Public Property Outcome As VerifyOutcome
        Public Property Log As ReportLog
        ''' <summary>For a genuine report: True if today's data for that period still gives the same fingerprint.</summary>
        Public Property DataUnchanged As Boolean
    End Class

End Namespace
