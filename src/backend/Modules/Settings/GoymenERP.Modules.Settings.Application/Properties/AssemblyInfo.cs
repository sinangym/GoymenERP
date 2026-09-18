using System.Runtime.CompilerServices;

// FİNANS #279 P3-1: the catalog-taking constructors are internal; only Settings' own tests
// may build a provider or catalog over entries other than the reviewed approved catalog.
[assembly: InternalsVisibleTo("GoymenERP.Settings.UnitTests")]
