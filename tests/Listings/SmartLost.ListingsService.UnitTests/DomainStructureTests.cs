using System.Reflection;
using System.Xml.Linq;
using SmartLost.BuildingBlocks.Core.Entities;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Domain.Entities;
using Xunit;

namespace SmartLost.ListingsService.UnitTests;

public sealed class DomainStructureTests
{
    [Fact]
    public void ManualDomainEntitiesPreserveModelledStructureAndEncapsulation()
    {
        var model = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Models", "listings.ecore"));
        Assembly assembly = typeof(Listing).Assembly;
        Assert.Equal("SmartLost.Contracts.Listings", typeof(ListingType).Assembly.GetName().Name);
        Assert.Null(assembly.GetType("SmartLost.ListingsService.Domain.Enums.ListingType"));
        const string RootNamespace = "SmartLost.ListingsService.Domain";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        foreach (XElement classifier in model.Root!.Elements("eClassifiers"))
        {
            string kind = classifier.Attribute(xsi + "type")!.Value;
            if (kind is not ("ecore:EClass" or "ecore:EEnum"))
            {
                continue;
            }

            string name = classifier.Attribute("name")!.Value;
            Type type = name == "ListingType" ? typeof(ListingType) : assembly.GetType(RootNamespace + (kind == "ecore:EClass" ? ".Entities." : ".Enums.") + name, throwOnError: true)!;
            if (kind == "ecore:EEnum")
            {
                Assert.Equal(classifier.Elements("eLiterals").Select(literal => literal.Attribute("name")!.Value), Enum.GetNames(type));
                foreach (XElement literal in classifier.Elements("eLiterals"))
                {
                    Assert.Equal(int.Parse(literal.Attribute("value")?.Value ?? "0", System.Globalization.CultureInfo.InvariantCulture),
                        Convert.ToInt32(Enum.Parse(type, literal.Attribute("name")!.Value), System.Globalization.CultureInfo.InvariantCulture));
                }

                continue;
            }

            Type expectedBase = name switch
            {
                "Listing" or "Category" or "MatchDecision" => typeof(AggregateRoot<Guid>),
                "Photo" or "Recovery" or "CredibilityEntry" => typeof(Entity<Guid>),
                "ListingCategory" => typeof(object),
                _ => throw new InvalidOperationException("Review the Domain role of the new model class: " + name)
            };
            Assert.Equal(expectedBase, type.BaseType);
            Assert.Empty(type.GetConstructors());
            object instance = Activator.CreateInstance(type, nonPublic: true)!;
            foreach (XElement feature in classifier.Elements("eStructuralFeatures"))
            {
                string featureName = feature.Attribute("name")!.Value;
                string member = char.ToUpperInvariant(featureName[0]) + featureName[1..];
                PropertyInfo property = type.GetProperty(member)!;
                Assert.NotNull(property);
                Assert.False(property.SetMethod?.IsPublic ?? false);
                string target = feature.Attribute("eType")!.Value.Split("#//", StringSplitOptions.None)[^1];
                Type clr = target switch
                {
                    "Guid" => typeof(Guid),
                    "ListingType" => typeof(ListingType),
                    "EString" => typeof(string),
                    "EInt" => typeof(int),
                    "ELong" => typeof(long),
                    "EDate" => name == "Listing" && featureName == "occurredOn" ? typeof(DateOnly) : typeof(DateTime),
                    _ => assembly.GetType(RootNamespace + (feature.Attribute(xsi + "type")!.Value == "ecore:EReference" ? ".Entities." : ".Enums.") + target, true)!
                };
                bool many = feature.Attribute("upperBound")?.Value == "-1";
                bool required = feature.Attribute("lowerBound")?.Value == "1";
                Type expected = many ? typeof(IReadOnlyCollection<>).MakeGenericType(clr)
                    : !required && clr.IsValueType ? typeof(Nullable<>).MakeGenericType(clr) : clr;
                Assert.Equal(expected, property.PropertyType);
                object? value = property.GetValue(instance);
                string? defaultValue = feature.Attribute("defaultValueLiteral")?.Value;
                if (defaultValue is not null)
                {
                    object expectedValue = clr.IsEnum ? Enum.Parse(clr, defaultValue)
                        : Convert.ChangeType(defaultValue, clr, System.Globalization.CultureInfo.InvariantCulture);
                    Assert.Equal(expectedValue, value);
                }
                else if (many)
                {
                    Assert.Empty(Assert.IsType<System.Collections.IEnumerable>(value, exactMatch: false));
                }


            }
        }

        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name!.StartsWith("NMF", StringComparison.Ordinal));
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name!.EndsWith("Modeling", StringComparison.Ordinal));
    }

    [Fact]
    public void CollectionViewsRejectExternalMutationsAndStayCurrentAfterBehaviorChanges()
    {
        var listing = Listing.CreateDraft(Guid.NewGuid(), ListingType.Lost);
        IReadOnlyCollection<ListingCategory> view = listing.Categories;
        var category = Category.Create("bag");
        listing.AddCategory(listing.AuthorId, listing.Revision, category);
        ListingCategory assignment = Assert.Single(view);
        Assert.Equal(listing.Id, assignment.ListingId);
        Assert.Equal(category.Id, assignment.CategoryId);
        Assert.Throws<NotSupportedException>(() => ((ICollection<ListingCategory>)view).Clear());
        Assert.Throws<NotSupportedException>(() => ((ICollection<string>)listing.Traits).Add("bypass"));
        listing.RemoveCategory(listing.AuthorId, listing.Revision, category.Id);
        Assert.Empty(view);
        Assert.Equal(listing.Id, assignment.ListingId);
        Assert.Equal(category.Id, assignment.CategoryId);
    }
}
