using System.Reflection;
using NMF.Expressions;
using NMF.Models;
using NMF.Models.Collections;
using NMF.Models.Meta;
using SmartLost.AuthService.Modeling.Auth;
using Xunit;

namespace SmartLost.AuthService.UnitTests;

public sealed class AuthModelTests
{
    [Fact]
    public void MetamodelPreservesTheAuthEntityStructureAndDefaults()
    {
        UserAccount user = new();
        RefreshSession session = new();
        RefreshToken token = new();

        Assert.Equal(0, user.IdentityVersion);
        Assert.Null(session.RevokedAtUtc);
        Assert.Empty(user.Sessions);
        Assert.Empty(session.Tokens);

        AssertClass(user, "UserAccount", 8, "sessions", true, "user");
        AssertClass(session, "RefreshSession", 4, "tokens", true, "session");
        AssertClass(token, "RefreshToken", 2, "session", false, "tokens");
    }

    [Fact]
    public void ContainmentAndInverseReferencesFollowAuthOwnership()
    {
        UserAccount user = new()
        {
            Id = Guid.NewGuid()
        };
        RefreshSession session = new()
        {
            Id = Guid.NewGuid()
        };
        RefreshToken token = new()
        {
            Id = Guid.NewGuid()
        };

        user.Sessions.Add(session);
        session.Tokens.Add(token);

        Assert.Same(user, session.User);
        Assert.Same(session, token.Session);
        Assert.Same(user, session.Parent);
        Assert.Same(session, token.Parent);

        token.Session = null!;
        Assert.Empty(session.Tokens);
        Assert.Null(token.Session);

        session.User = null!;
        Assert.Empty(user.Sessions);
        Assert.Null(session.User);
    }

    [Theory]
    [InlineData("UserAccount")]
    [InlineData("RefreshSession")]
    [InlineData("RefreshToken")]
    public void ScalarAttributesSupportReflectiveAccessAndNotifications(string className)
    {
        IModelInspection inspection = Create(className);
        ModelElement element = inspection.Element;
        List<string?> changes = [];
        element.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        foreach (IAttribute attribute in element.GetClass().Attributes)
        {
            object first = AttributeValue(attribute.Name, false);
            object second = AttributeValue(attribute.Name, true);
            string propertyName = char.ToUpperInvariant(attribute.Name[0]) + attribute.Name[1..];
            EventInfo? changingEvent = element.GetType().GetEvent(propertyName + "Changing");
            EventInfo? changedEvent = element.GetType().GetEvent(propertyName + "Changed");
            Assert.NotNull(changingEvent);
            Assert.NotNull(changedEvent);
            ValueChangedEventArgs? changing = null;
            ValueChangedEventArgs? changed = null;
            EventHandler<ValueChangedEventArgs> onChanging = (_, args) => changing = args;
            EventHandler<ValueChangedEventArgs> onChanged = (_, args) => changed = args;
            changingEvent.AddEventHandler(element, onChanging);
            changedEvent.AddEventHandler(element, onChanged);
            INotifyExpression<object> expression = inspection.AttributeExpression(attribute.Name.ToUpperInvariant());
            expression.Successors.SetDummy();

            try
            {
                element.SetAttributeValue(attribute, first);
                Assert.Equal(first, element.GetAttributeValue(attribute));
                Assert.Equal(first, expression.Value);

                if (expression is INotifyReversableExpression<string> text)
                {
                    text.Value = (string)second;
                }
                else
                {
                    INotifyReversableExpression<object> reversible = Assert.IsType<INotifyReversableExpression<object>>(expression, exactMatch: false);
                    reversible.Value = second;
                }

                Assert.Equal(second, element.GetAttributeValue(attribute));
                Assert.Equal(second, expression.Value);
                Assert.NotNull(changing);
                Assert.NotNull(changed);
                Assert.Equal(first, changing.OldValue);
                Assert.Equal(second, changing.NewValue);
                Assert.Equal(first, changed.OldValue);
                Assert.Equal(second, changed.NewValue);
            }
            finally
            {
                expression.Successors.UnsetAll();
                changingEvent.RemoveEventHandler(element, onChanging);
                changedEvent.RemoveEventHandler(element, onChanged);
            }
        }

        Assert.All(element.GetClass().Attributes,
            attribute => Assert.Contains(attribute.Name, changes, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void IdentifiedElementsResolveAndMoveBetweenModels()
    {
        Model first = new()
        {
            ModelUri = new Uri("https://smartlost.example/samples/auth-first.xmi")
        };
        Model second = new()
        {
            ModelUri = new Uri("https://smartlost.example/samples/auth-second.xmi")
        };
        ModelElement[] elements = [new UserAccount(), new RefreshSession(), new RefreshToken()];

        foreach (ModelElement element in elements)
        {
            IAttribute id = element.GetClass().Attributes.Single(attribute => attribute.Name == "id");
            element.SetAttributeValue(id, Guid.NewGuid());
            first.RootElements.Add(element);

            string identifier = element.ToIdentifierString();
            Assert.True(element.IsIdentified);
            Assert.Same(element, first.Resolve(identifier));

            element.SetAttributeValue(id, Guid.NewGuid());
            Assert.Null(first.Resolve(identifier));
            Assert.Same(element, first.Resolve(element.ToIdentifierString()));

            second.RootElements.Add(element);
            Assert.Same(second, element.Model);
            Assert.Null(first.Resolve(element.ToIdentifierString()));
            Assert.Same(element, second.Resolve(element.ToIdentifierString()));
        }
    }

    [Fact]
    public void ReferenceExpressionsAndCollectionAdaptersMaintainTheOwnershipGraph()
    {
        InspectableUserAccount user = new();
        InspectableRefreshSession session = new();
        InspectableRefreshToken token = new();
        ReferenceCollection userChildren = new UserAccount.UserAccountChildrenCollection(user);
        ReferenceCollection sessionChildren = new RefreshSession.RefreshSessionChildrenCollection(session);
        ReferenceCollection sessionReferences = new RefreshSession.RefreshSessionReferencedElementsCollection(session);
        ReferenceCollection tokenReferences = new RefreshToken.RefreshTokenReferencedElementsCollection(token);

        INotifyCollection<IModelElement> observedChildren = userChildren.AsNotifiable();
        observedChildren.Successors.SetDummy();
        try
        {
            userChildren.Add(session);
            sessionChildren.Add(token);
            Assert.Contains(session, userChildren);
            Assert.Contains(token, sessionChildren);
            Assert.Contains(user, sessionReferences);
            Assert.Contains(token, sessionReferences);
            Assert.Contains(session, tokenReferences);

            INotifyExpression<IModelElement> userExpression = session.ReferenceExpression("USER");
            INotifyExpression<IModelElement> sessionExpression = token.ReferenceExpression("SESSION");
            userExpression.Successors.SetDummy();
            sessionExpression.Successors.SetDummy();
            try
            {
                Assert.Same(user, userExpression.Value);
                Assert.Same(session, sessionExpression.Value);
                Assert.True(sessionChildren.Remove(token));
                Assert.True(userChildren.Remove(session));
                Assert.Null(userExpression.Value);
                Assert.Null(sessionExpression.Value);
            }
            finally
            {
                userExpression.Successors.UnsetAll();
                sessionExpression.Successors.UnsetAll();
            }
        }
        finally
        {
            observedChildren.Successors.UnsetAll();
        }
    }

    [Fact]
    public void CollectionAdaptersSupportEnumerationCopiesRemovalAndClearing()
    {
        UserAccount user = new();
        RefreshSession session = new();
        ExerciseCollection(new UserAccount.UserAccountChildrenCollection(user), session, new RefreshToken());
        ExerciseCollection(new UserAccount.UserAccountReferencedElementsCollection(user), session, new RefreshToken());

        RefreshSession tokenSession = new();
        RefreshToken token = new();
        ExerciseCollection(new RefreshSession.RefreshSessionChildrenCollection(tokenSession), token, new UserAccount());

        RefreshSession referencedSession = new();
        UserAccount referencedUser = new();
        RefreshToken referencedToken = new();
        var references = new RefreshSession.RefreshSessionReferencedElementsCollection(referencedSession)
        {
            referencedUser,
            referencedToken
        };
        Assert.Equal(2, references.Count);
        Assert.True(references.Remove(referencedUser));
        Assert.True(references.Remove(referencedToken));
        Assert.Empty(references);

        RefreshToken tokenWithReference = new();
        ReferenceCollection tokenReferences = new RefreshToken.RefreshTokenReferencedElementsCollection(tokenWithReference)
        {
            new RefreshSession()
        };
        Assert.Single(tokenReferences);
        tokenReferences.Clear();
        Assert.Empty(tokenReferences);
    }

    private static void AssertClass(ModelElement element, string name, int attributeCount, string referenceName, bool containment, string oppositeName)
    {
        IClass modelClass = element.GetClass();
        IReference reference = modelClass.References.Single(candidate => candidate.Name == referenceName);

        Assert.Equal(name, modelClass.Name);
        Assert.Equal(attributeCount, modelClass.Attributes.Count);
        Assert.Equal(containment, reference.IsContainment);
        Assert.Equal(oppositeName, reference.Opposite.Name);
    }

    private static void ExerciseCollection(ReferenceCollection collection, IModelElement expected, IModelElement unrelated)
    {
        collection.Add(expected);
        collection.Add(unrelated);
        Assert.Single(collection);
        Assert.Contains(expected, collection);
        Assert.DoesNotContain(unrelated, collection);
        Assert.False(collection.Remove(unrelated));

        var copy = new IModelElement[2];
        collection.CopyTo(copy, 1);
        Assert.Same(expected, copy[1]);
        Assert.Single(collection.ToArray());
        Assert.True(collection.Remove(expected));
        Assert.Empty(collection);

        collection.Add(expected);
        collection.Clear();
        Assert.Empty(collection);
    }

    private static IModelInspection Create(string className)
    {
        return className switch
        {
            "UserAccount" => new InspectableUserAccount(),
            "RefreshSession" => new InspectableRefreshSession(),
            "RefreshToken" => new InspectableRefreshToken(),
            _ => throw new ArgumentException("Unsupported Auth model class.", nameof(className))
        };
    }

    private static object AttributeValue(string name, bool alternate)
    {
        return name switch
        {
            "id" => alternate
                ? Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002")
                : Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            "identityVersion" => alternate ? 3L : 2L,
            "createdAtUtc" or "expiresAtUtc" or "revokedAtUtc" => new DateTime(2026, 10, alternate ? 2 : 1, 0, 0, 0, DateTimeKind.Utc),
            _ => alternate ? "updated-value" : "initial-value"
        };
    }

    private interface IModelInspection
    {
        ModelElement Element
        {
            get;
        }

        INotifyExpression<object> AttributeExpression(string name);

        INotifyExpression<IModelElement> ReferenceExpression(string name);
    }

    private sealed class InspectableUserAccount : UserAccount, IModelInspection
    {
        public ModelElement Element => this;

        public INotifyExpression<object> AttributeExpression(string name)
        {
            return GetExpressionForAttribute(name);
        }

        public INotifyExpression<IModelElement> ReferenceExpression(string name)
        {
            return GetExpressionForReference(name);
        }
    }

    private sealed class InspectableRefreshSession : RefreshSession, IModelInspection
    {
        public ModelElement Element => this;

        public INotifyExpression<object> AttributeExpression(string name)
        {
            return GetExpressionForAttribute(name);
        }

        public INotifyExpression<IModelElement> ReferenceExpression(string name)
        {
            return GetExpressionForReference(name);
        }
    }

    private sealed class InspectableRefreshToken : RefreshToken, IModelInspection
    {
        public ModelElement Element => this;

        public INotifyExpression<object> AttributeExpression(string name)
        {
            return GetExpressionForAttribute(name);
        }

        public INotifyExpression<IModelElement> ReferenceExpression(string name)
        {
            return GetExpressionForReference(name);
        }
    }
}
