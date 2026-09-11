# SmartInput Independent Code Review Packet

You are reviewing a real .NET 8 Windows application. The attached files are the production decision pipeline and the corpus verifier. Do not provide generic advice. Trace concrete candidate loss and explain why production applies a correction when the independent verifier says `Wait`.

## Primary acceptance target

The seed-42 audit must reach:

- `BF_vs_independent_oracle_disagreement = 0`;
- `ProductionSignatureIndexMiss = 0`;
- `TotalAmbiguousApplied = 0`;
- `WrongUniqueTarget = 0`;
- `WrongDirectLayout = 0`;
- `WrongCombined = 0`;
- `ExactWordsChanged = 0`;
- unique recovery >= 95%;
- all 104 mandatory assertions passing.

Do not solve this by disabling automation globally, adding a large denylist, or making the production classifier call the oracle. The oracle must remain independent.

## Real regressions that must remain correct

Must apply:

```text
ghbdtn -> привет
руддщ -> hello
мущ -> veo
пзг -> gpu
мущ 3 -> veo 3
миняй -> меняй
vbyzq -> меняй
gtie -> пишу
превет -> привет
деелай -> делай
машиина -> машина
говноо -> говно
будуут -> будут
напесал -> написал
исслидование -> исследование
helo -> hello
teh -> the
adn -> and
```

Must preserve exactly:

```text
начала, начало, дает, даёт, даст, дают, дать, жать,
дал, дела, дело, видел, видик, нас, нам, меня, сеня,
неизвестное, неизвестно, гавно, говно, душ, пишу,
все, всё, еще, ещё, hello, world, the, and, help
```

Protected tokens must remain unchanged: URLs, emails, paths, identifiers, mixed-script tokens, technical names, and exact known words.

## Questions to answer from the code

1. Which exact method causes a candidate/competitor to disappear from `ProductionSignatureIndex`?
2. Are candidate generation, edit provenance, canonical signatures and ambiguity bands identical between the verifier and production?
3. Can the `MaxGeneratedCandidates` bound make results dependent on generation order?
4. Does production filter candidates before ambiguity is computed?
5. Are cross-operation competitors considered (repeated, transposition, insertion, deletion, adjacent-key, vowel and general substitution)?
6. Why do the 420 brute-force/oracle disagreements occur, especially for general substitution?
7. Why do exact known words remain protected independently of frequency?
8. What is the smallest safe production fix, and what regression tests prove it?

## Required investigation method

For at least 20 concrete `ProductionSignatureIndexMiss` cases and all BF/oracle disagreement classes, produce a table containing only case id and metadata (never raw text in logs):

```text
case id
mutation operation
oracle target operation
oracle candidate set size
production candidate set size
missing competitor signature
stage where it disappeared
final production decision
expected independent decision
```

Then implement or describe a fix that makes the two candidate spaces agree without coupling the oracle to production. Re-run seed 42, held-out seeds 1337 and 20260903, operation-cluster tests, mandatory 104 assertions, stress tests, application-policy matrix and the full Core/App suite.

## Safety and privacy invariants

Keep Safe Mode, secure-input, unknown-context, excluded-application, emergency-pause and Protection gates unchanged. Safe Mode blocks live automation but permits manual external Fix actions. No token, candidate, prediction context or replacement text may be written to logs, metrics or diagnostic DTOs.

## Expected response

Return:

1. concrete root causes with file/class/method names;
2. evidence from the supplied code and test output;
3. minimal patch design;
4. new tests, including the Russian examples above;
5. a gate-by-gate before/after report;
6. remaining uncertainty.

Do not claim completion while any ambiguous or wrong-apply counters are non-zero.
