# Listings contracts

Framework-free .NET 10 contract types shared by Listings and Matching. This project
has no project references, NuGet dependencies, entities, persistence or runtime host.

`ListingType` is the runtime definition of `Lost = 0` and `Found = 1`. Listings Domain
uses this enum. Matching Domain's scaffold references this library in preparation for
integration with its runtime implementation on main. At that merge, replace Matching's
local Domain enum and imports with this contract; no runtime Matching code is brought
into this branch. Neither service references the other's implementation projects.

Do not renumber or reuse existing values. New values require compatible consumers;
a shared source definition does not upgrade deployed services automatically. Validate
incoming values before using them in domain operations.

NMF Modeling projects retain generated enums as separate structural models, without
runtime references from services. The existing model tests check both generated enums
against this contract, including names and numeric values. No message delivery or API
integration is implemented by this library.

The project is listed in the root, Listings and Matching solutions. See the
[root developer guide](../../../README.md), [Listings](../../Services/Listings/README.md)
and [Matching](../../Services/Matching/README.md) for build and data ownership.
