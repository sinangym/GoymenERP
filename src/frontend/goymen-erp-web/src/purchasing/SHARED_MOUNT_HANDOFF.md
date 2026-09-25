# Purchasing normal-host handoff

Owner scope implements P-03 list, P-03-DETAIL and the existing approve command control.
P-04 remains staged because Product purchase eligibility/candidate selection is not published.

Central single-writer work after the owner commit is fixed:

- add the Purchasing capability ids, paths and list-only navigation label to the shell catalogue;
- spread `purchasingRoutes` into the central router, static list before parameterized detail;
- accept the new owner declaration hash and recompute only the authority combinations that include Purchasing;
- adopt owner contract `PURCHASING/1.1`: unlike the Logistics availability-only flip, this set adds the approve child declaration; update only the Purchasing-containing authority names/hashes after the final owner tuple;
- publish list, detail and the detail's approve child control; keep create unavailable and without navigation;
- run the normal-profile Purchasing browser suite only after central composition, using a real persisted Draft order and two actors where the reader cannot approve;
- do not alias Sales E1, add a grant, create a Product candidate source or claim P-04 acceptance.
