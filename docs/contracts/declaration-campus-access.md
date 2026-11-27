# Declaration campus access

Direct external declarations use the authenticated actor's campus. A legacy
submission's `campusCode` request field cannot choose a different review queue;
the server derives the stored campus from the actor, as the canonical submission
path already does. Existing records are not reassigned by this change.

For legacy records without a canonical contribution:

- A student may read their own declaration.
- CTSV role access to another student's direct external declaration requires
  `IsOutsideClub == true`, no `ClubId`, and the same campus as the actor.
- System Admin retains cross-campus access to direct external declarations.
- Explicit scoped read grants retain their existing behavior.
- A direct review from a foreign-campus CTSV actor returns HTTP 403 and makes
  no state change. Club-linked legacy records still require the versioned
  contribution workflow; they cannot use the direct staff-review shortcut.
- Existing self-review, terminal-state and validation guards remain in effect.

Queue, detail and review access must agree. A record omitted from a staff
member's campus queue must not become accessible through a guessed detail ID
or direct review URL solely because that actor has the CTSV role.

Canonical contributions continue through their existing versioned review and
campus/delegated-authorization checks. This patch does not migrate legacy points
to the authoritative XP ledger or change the scoring formula.

`CampusDeclarationQueueTests` covers canonical declarations, legacy declarations
in HAN/HCM/DAN, direct cross-campus access/review denial, unchanged state after
denial, owner/Admin/same-campus reads, same-campus review and a forged submission
campus.
