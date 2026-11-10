Option Strict On
Option Explicit On
Option Infer On

Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.Models
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

''' <summary>Create or edit a package (name, inclusions, price) or an add-on (name, price).</summary>
Public Class PriceItemDialog
    Inherits LsFormDialog

    Private Const FieldBlock As Integer = 92
    Private Const InclusionsHeight As Integer = 96

    Private ReadOnly _kind As PriceItemKind
    Private ReadOnly _existing As PriceItem
    Private ReadOnly _txtName As New LsTextBox()
    Private ReadOnly _txtInclusions As New LsTextBox()
    Private ReadOnly _txtPrice As New LsTextBox()

    Public Property SavedId As Integer

    Public Sub New(kind As PriceItemKind, Optional existing As PriceItem = Nothing)
        MyBase.New(If(existing Is Nothing, "New " & PricingAdmin.Noun(kind), "Edit " & PricingAdmin.Noun(kind)),
                   If(existing Is Nothing, If(kind = PriceItemKind.Package, "Add a package", "Add an add-on"), existing.Name),
                   If(kind = PriceItemKind.Package, "Customers book packages; the price is billed in Add Service.",
                                                     "Extras billed on top of a package, e.g. prints or frames."),
                   If(existing Is Nothing, "Add " & PricingAdmin.Noun(kind), "Save changes"), "Cancel", 380,
                   BodyHeight(kind))
        _kind = kind
        _existing = existing

        Dim y = 0
        AddField("Name", _txtName, y, Theme.Dpi(42))
        y += Theme.Dpi(FieldBlock)
        If kind = PriceItemKind.Package Then
            _txtInclusions.Multiline = True
            _txtInclusions.MaxLength = PricingAdmin.MaxInclusions
            _txtInclusions.Placeholder = "e.g. 1-hour shoot, 15 edited photos, 1 pc 8R print"
            AddField("Inclusions", _txtInclusions, y, Theme.Dpi(InclusionsHeight))
            y += Theme.Dpi(InclusionsHeight + FieldBlock - 42)
        End If
        AddField("Price (₱)", _txtPrice, y, Theme.Dpi(42))
        _txtName.MaxLength = 100
        _txtName.Placeholder = If(kind = PriceItemKind.Package, "e.g. Family Portrait", "e.g. Extra 8R print")
        _txtPrice.Filter = InputFilter.Money
        _txtPrice.MaxLength = 13
        _txtPrice.Placeholder = "e.g. 1,500.00"
        AddHandler _txtPrice.InnerTextBox.Leave, AddressOf FormatPrice

        If existing IsNot Nothing Then
            _txtName.Text = existing.Name
            _txtInclusions.Text = existing.Inclusions
            _txtPrice.Text = Money.FormatPlain(existing.Price)
        End If
    End Sub

    Private Shared Function BodyHeight(kind As PriceItemKind) As Integer
        Return If(kind = PriceItemKind.Package, FieldBlock * 2 + InclusionsHeight - 42 + FieldBlock - 26, FieldBlock * 2 - 26) + 24
    End Function

    Private Sub AddField(caption As String, box As LsTextBox, y As Integer, height As Integer)
        Dim lbl As New Label() With {
            .Text = caption, .Font = Theme.LabelBold, .ForeColor = Theme.Charcoal, .TextAlign = ContentAlignment.BottomLeft,
            .Bounds = New Rectangle(0, y, Body.Width, Theme.LabelBold.Height + Theme.Dpi(4))}
        box.FieldName = caption
        box.Bounds = New Rectangle(0, lbl.Bottom + Theme.Dpi(4), Body.Width, height)
        Body.Controls.Add(lbl)
        Body.Controls.Add(box)
    End Sub

    Private Sub FormatPrice(sender As Object, e As EventArgs)
        Dim amount As Decimal
        Dim problem As String = Nothing
        If Money.TryParse(_txtPrice.Text, amount, problem) Then _txtPrice.Text = Money.FormatPlain(amount)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        _txtName.Focus()
    End Sub

    Protected Overrides Async Function OnSubmitAsync() As Task(Of Boolean)
        Validator.ClearAll(Body)
        Dim p = PricingAdmin.NameProblem(_txtName.Text)
        If p IsNot Nothing Then Validator.SetError(_txtName, p)
        Dim price As Decimal
        Dim problem As String = Nothing
        If Not Money.TryParse(_txtPrice.Text, price, problem) Then
            Validator.SetError(_txtPrice, problem)
        Else
            p = PricingAdmin.PriceProblem(price)
            If p IsNot Nothing Then Validator.SetError(_txtPrice, p)
        End If
        If Validator.HasErrors(Body) Then
            Shake()
            Validator.FocusFirstError(Body)
            Return False
        End If

        Dim item As New PriceItem With {
            .Id = If(_existing Is Nothing, 0, _existing.Id), .Name = _txtName.Text, .Inclusions = _txtInclusions.Text, .Price = price}
        Dim kind = _kind
        SetBusy(True, "Saving...")
        Dim result = Await Task.Run(Function() PricingAdmin.Save(kind, item))
        SetBusy(False)
        Dim fields As New Dictionary(Of String, Control) From {{"name", _txtName}, {"inclusions", _txtInclusions}, {"price", _txtPrice}}
        If FormErrors.Show(result, fields) Then
            Shake()
            Return False
        End If
        SavedId = CInt(result.Value)
        Return True
    End Function

End Class
