Option Strict On
Option Explicit On
Option Infer On

Imports LenSync.Data
Imports LenSync.Models

Namespace Core

    ''' <summary>Settings > Packages and Settings > Add-ons (create, edit, deactivate/reactivate; never delete).</summary>
    Public NotInheritable Class PricingAdmin

        Private Sub New()
        End Sub

        Public Const MaxInclusions As Integer = 1000

        Public Shared Function Noun(kind As PriceItemKind) As String
            Return If(kind = PriceItemKind.Package, "package", "add-on")
        End Function

        Public Shared Function NameProblem(name As String) As String
            name = If(name, "").Trim()
            If name.Length < 2 Then Return "Enter a name."
            If name.Length > 100 Then Return "Use at most 100 characters."
            Return Nothing
        End Function

        Public Shared Function PriceProblem(price As Decimal) As String
            If price <= 0D Then Return "The price must be more than ₱0.00."
            If price > Money.MaxAmount Then Return $"The price can't be more than {Money.Format(Money.MaxAmount)}."
            Return Nothing
        End Function

        Public Shared Function List(kind As PriceItemKind) As List(Of PriceItem)
            Session.Demand(Permissions.Settings)
            Return PriceItemRepository.List(kind)
        End Function

        ''' <summary>Creates (Id = 0) or updates an item. Value = the item's id.</summary>
        Public Shared Function Save(kind As PriceItemKind, item As PriceItem) As AdminResult
            Session.Demand(Permissions.Settings)
            item.Name = If(item.Name, "").Trim()
            item.Inclusions = If(kind = PriceItemKind.Package, If(item.Inclusions, "").Trim(), "")
            Dim result As New AdminResult()
            Dim p = NameProblem(item.Name)
            If p IsNot Nothing Then
                result.AddError("name", p)
            ElseIf PriceItemRepository.ActiveNameExists(kind, item.Name, item.Id) Then
                result.AddError("name", $"An active {Noun(kind)} already has this name.")
            End If
            If item.Inclusions.Length > MaxInclusions Then result.AddError("inclusions", $"Use at most {MaxInclusions} characters.")
            p = PriceProblem(item.Price) : If p IsNot Nothing Then result.AddError("price", p)
            If Not result.Ok Then Return result

            Dim isPackage = kind = PriceItemKind.Package
            If item.Id = 0 Then
                Dim newId = Db.InTransaction(Of Integer)(
                    Function(conn, tx)
                        Dim id = PriceItemRepository.Insert(conn, tx, kind, item)
                        AuditLog.Write(conn, tx, If(isPackage, AuditActions.PackageCreate, AuditActions.AddonCreate), Table(kind), id,
                                       $"Added {Noun(kind)} '{item.Name}' at {Money.Format(item.Price)}")
                        Return id
                    End Function)
                Return AdminResult.Success(newId)
            End If

            Dim existing = PriceItemRepository.GetById(kind, item.Id)
            If existing Is Nothing Then Return AdminResult.Fail("", $"This {Noun(kind)} no longer exists.")
            Dim changes As New List(Of String)
            If existing.Name <> item.Name Then changes.Add($"name '{existing.Name}' -> '{item.Name}'")
            If existing.Price <> item.Price Then changes.Add($"price {Money.Format(existing.Price)} -> {Money.Format(item.Price)}")
            If isPackage AndAlso existing.Inclusions <> item.Inclusions Then changes.Add("inclusions changed")
            If changes.Count = 0 Then Return AdminResult.Success(item.Id)
            Db.InTransaction(
                Sub(conn, tx)
                    PriceItemRepository.Update(conn, tx, kind, item)
                    AuditLog.Write(conn, tx, If(isPackage, AuditActions.PackageUpdate, AuditActions.AddonUpdate), Table(kind), item.Id,
                                   $"Updated {Noun(kind)} '{existing.Name}': " & String.Join("; ", changes))
                End Sub)
            Return AdminResult.Success(item.Id)
        End Function

        Public Shared Function SetActive(kind As PriceItemKind, id As Integer, active As Boolean) As AdminResult
            Session.Demand(Permissions.Settings)
            Dim existing = PriceItemRepository.GetById(kind, id)
            If existing Is Nothing Then Return AdminResult.Fail("", $"This {Noun(kind)} no longer exists.")
            If existing.IsActive = active Then Return AdminResult.Success()
            If active AndAlso PriceItemRepository.ActiveNameExists(kind, existing.Name, id) Then
                Return AdminResult.Fail("", $"Another active {Noun(kind)} is named '{existing.Name}'. Rename one of them first.")
            End If
            Dim isPackage = kind = PriceItemKind.Package
            Dim action = If(isPackage, If(active, AuditActions.PackageActivate, AuditActions.PackageDeactivate),
                                       If(active, AuditActions.AddonActivate, AuditActions.AddonDeactivate))
            Db.InTransaction(
                Sub(conn, tx)
                    PriceItemRepository.SetActive(conn, tx, kind, id, active)
                    AuditLog.Write(conn, tx, action, Table(kind), id, $"{If(active, "Reactivated", "Deactivated")} {Noun(kind)} '{existing.Name}'")
                End Sub)
            Return AdminResult.Success()
        End Function

        Private Shared Function Table(kind As PriceItemKind) As String
            Return If(kind = PriceItemKind.Package, "packages", "addons")
        End Function

    End Class

End Namespace
