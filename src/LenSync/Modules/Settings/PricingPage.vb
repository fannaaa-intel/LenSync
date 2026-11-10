Option Strict On
Option Explicit On
Option Infer On

Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.Models
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

Namespace Modules.Settings

    ''' <summary>Settings > Packages and Settings > Add-ons (same page, different columns).</summary>
    Public Class PricingPage
        Inherits SettingsPage

        Private ReadOnly _kind As PriceItemKind
        Private ReadOnly _search As New LsTextBox()
        Private ReadOnly _showInactive As New LsCheck("Show inactive")
        Private ReadOnly _grid As New LsGrid()
        Private ReadOnly _btnNew As LsButton
        Private ReadOnly _btnEdit As New LsButton("Edit", ButtonVariant.Secondary)
        Private ReadOnly _btnActive As New LsButton("Deactivate", ButtonVariant.Secondary)
        Private _items As New List(Of PriceItem)

        Public Sub New(kind As PriceItemKind)
            MyBase.New(If(kind = PriceItemKind.Package, "Packages", "Add-ons"),
                       If(kind = PriceItemKind.Package,
                          "Packages customers can book. Price changes apply to new bills; past receipts keep their price.",
                          "Extras added to a bill, like prints, frames or rush editing."))
            _kind = kind
            _btnNew = New LsButton(If(kind = PriceItemKind.Package, "New package", "New add-on"), ButtonVariant.Primary)
            AddAction(_btnNew)
            _search.Placeholder = If(kind = PriceItemKind.Package, "Search packages", "Search add-ons")
            _search.FieldName = _search.Placeholder
            _showInactive.Width = _showInactive.PreferredWidth()
            _grid.EmptyText = If(kind = PriceItemKind.Package, "No packages to show.", "No add-ons to show.")
            _grid.AddTextColumn("name", "Name", 26)
            If kind = PriceItemKind.Package Then _grid.AddTextColumn("inclusions", "Inclusions", 46)
            _grid.AddTextColumn("price", "Price", 14, alignRight:=True)
            _grid.AddStatusColumn("status", "Status", 12)
            Controls.AddRange({_search, _showInactive, _btnEdit, _btnActive, _grid})

            AddHandler _search.TextChanged, Sub() Fill()
            AddHandler _showInactive.CheckedChanged, Sub() Fill()
            AddHandler _grid.SelectionChanged, Sub() UpdateButtons()
            AddHandler _grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then EditSelected()
            AddHandler _grid.KeyDown, Sub(s, e)
                                          If e.KeyCode = Keys.Enter Then
                                              e.Handled = True
                                              EditSelected()
                                          End If
                                      End Sub
            AddHandler _btnNew.Click, Sub() CreateItem()
            AddHandler _btnEdit.Click, Sub() EditSelected()
            AddHandler _btnActive.Click, Sub() ToggleSelected()
            UpdateButtons()
        End Sub

        Public Overrides Async Function ReloadAsync() As Task
            Dim kind = _kind
            Dim data = Await RunAsync(Function() PricingAdmin.List(kind), "Loading " & PricingAdmin.Noun(kind) & "s")
            If data Is Nothing Then Return
            _items = data
            Fill()
        End Function

        Private Sub Fill(Optional selectId As Integer = 0)
            Dim keepId = If(selectId <> 0, selectId, If(TryCast(_grid.SelectedItem, PriceItem)?.Id, 0))
            Dim q = _search.Text.Trim()
            _grid.Rows.Clear()
            For Each item In _items
                If Not item.IsActive AndAlso Not _showInactive.Checked AndAlso item.Id <> selectId Then Continue For
                If q.Length > 0 AndAlso item.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                   item.Inclusions.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
                Dim status = If(item.IsActive, "Active", "Inactive")
                Dim index As Integer
                If _kind = PriceItemKind.Package Then
                    index = _grid.Rows.Add(item.Name, item.Inclusions.Replace(vbCrLf, " · ").Replace(vbLf, " · "), Money.Format(item.Price), status)
                    _grid.Rows(index).Cells("inclusions").ToolTipText = item.Inclusions
                Else
                    index = _grid.Rows.Add(item.Name, Money.Format(item.Price), status)
                End If
                _grid.Rows(index).Tag = item
            Next
            If keepId <> 0 Then _grid.SelectWhere(Function(t) DirectCast(t, PriceItem).Id = keepId)
            UpdateButtons()
        End Sub

        Private ReadOnly Property Selected As PriceItem
            Get
                Return TryCast(_grid.SelectedItem, PriceItem)
            End Get
        End Property

        Private Sub UpdateButtons()
            Dim item = Selected
            _btnEdit.Enabled = item IsNot Nothing
            _btnActive.Enabled = item IsNot Nothing
            _btnActive.Text = If(item IsNot Nothing AndAlso Not item.IsActive, "Reactivate", "Deactivate")
            _btnActive.FitToText()
            PerformLayout()
        End Sub

        Protected Overrides Sub LayoutContent(area As Rectangle)
            Dim gap = Theme.Dpi(10)
            Dim y = area.Top
            Dim right = area.Right
            For Each b In {_btnActive, _btnEdit}
                b.Location = New Point(right - b.Width, y + 1)
                right = b.Left - gap
            Next
            Dim searchW = Math.Max(Theme.Dpi(160), Math.Min(Theme.Dpi(320), right - area.Left - _showInactive.Width - Theme.Dpi(40)))
            _search.Bounds = New Rectangle(area.Left, y, searchW, Theme.Dpi(40))
            _showInactive.Location = New Point(_search.Right + Theme.Dpi(20), y + (Theme.Dpi(40) - _showInactive.Height) \ 2)
            Dim gridTop = _search.Bottom + Theme.Dpi(16)
            _grid.Bounds = New Rectangle(area.Left, gridTop, area.Width, Math.Max(0, area.Bottom - gridTop))
        End Sub

        ' ---------------------------------------------------------------- actions
        Private Async Sub CreateItem()
            Dim newId As Integer
            Using dlg As New PriceItemDialog(_kind)
                If dlg.ShowCard() <> DialogResult.OK Then Return
                newId = dlg.SavedId
            End Using
            Await ReloadAsync()
            Fill(newId)
            Toast.Show($"{Capitalized()} added.", ToastKind.Success)
        End Sub

        Private Async Sub EditSelected()
            Dim item = Selected
            If item Is Nothing Then Return
            Using dlg As New PriceItemDialog(_kind, item)
                If dlg.ShowCard() <> DialogResult.OK Then Return
            End Using
            Await ReloadAsync()
            Fill(item.Id)
            Toast.Show($"{Capitalized()} saved.", ToastKind.Success)
        End Sub

        Private Async Sub ToggleSelected()
            Dim item = Selected
            If item Is Nothing Then Return
            Dim activate = Not item.IsActive
            If Not activate AndAlso Not LsDialog.Confirm(
                $"'{item.Name}' will no longer be offered for new bookings and bills. Past receipts are not changed.",
                $"Deactivate this {PricingAdmin.Noun(_kind)}?", "Deactivate", "Keep active") Then Return
            Dim kind = _kind
            Dim id = item.Id
            Dim result = Await RunAsync(Function() PricingAdmin.SetActive(kind, id, activate), "Changing status")
            If result Is Nothing OrElse FormErrors.Show(result, Nothing) Then Return
            Await ReloadAsync()
            Fill(id)
            Toast.Show(If(activate, $"'{item.Name}' is available again.", $"'{item.Name}' was deactivated."), ToastKind.Success)
        End Sub

        Private Function Capitalized() As String
            Dim n = PricingAdmin.Noun(_kind)
            Return Char.ToUpperInvariant(n(0)) & n.Substring(1)
        End Function

    End Class

End Namespace
