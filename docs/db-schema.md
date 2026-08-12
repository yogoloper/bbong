# BBONG DB 스키마 확장 설계

> 작성 2026-08-12. 대상은 `server/BbongServer`의 PostgreSQL 스키마입니다.
> 로드맵은 `mobile-roadmap.md`, 구조는 `mobile-architecture.md`, 리스크는 `considerations.md`를 따릅니다.
> 이 문서는 설계만 담고 마이그레이션 코드는 포함하지 않습니다.

## 0. 요약

지금 스키마의 문제는 테이블이 적다는 것이 아니라, 있는 테이블에서 **시간과 인과가 빠져 있다**는 것입니다.
원장(`ledger`)에 시각 컬럼이 없습니다. 무엇이 언제 일어났는지 DB만 보고는 알 수 없고, 어떤 게임이나
어떤 구매가 그 변동을 만들었는지도 알 수 없습니다. IAP를 붙이는 순간 이 두 가지가 그대로 환불 대응
불가로 이어집니다. 미성년자 환불 요청이 들어오면 "언제 얼마를 샀고 그 포인트가 어디까지 소진됐는지"를
답해야 하는데, 지금 데이터로는 답이 안 나옵니다.

그래서 우선순위는 IAP 테이블을 만드는 것이 아니라 원장을 먼저 고치는 쪽입니다. 원장이 시각과 참조를
갖게 되면 IAP 테이블은 그 위에 얹기만 하면 되고, 반대 순서로 하면 IAP 테이블을 만들고도 정합을 못 맞춥니다.

---

## 1. 현재 스키마 실측

`BbongDbContext.cs`, `Migrations/BbongDbContextModelSnapshot.cs`, 각 `*Row.cs`와 도메인 클래스를 읽어
확인한 내용입니다. 운영 DB에 `__EFMigrationsHistory`를 포함해 8개 테이블이 있다는 것은 전달받은
정보이고, 아래 7개는 코드에서 확인했습니다.

| 테이블 | 컬럼 | 인덱스 | 정의 위치 |
|---|---|---|---|
| `accounts` | Id(uuid PK), Nickname(varchar 12), CreatedAt(timestamptz), Provider(text null), SocialSubject(text null) | (Provider, SocialSubject) UNIQUE, `WHERE "Provider" IS NOT NULL` | `Domain/Accounts/UserAccount.cs`, 매핑은 `BbongDbContext.cs:30-43` |
| `ledger` | Id(bigint identity PK), UserId(uuid), Delta(bigint), Reason(text) | (UserId) | `Infrastructure/Persistence/LedgerRow.cs` |
| `ad_rewards` | Id(bigint identity PK), UserId(uuid), Kind(text), ClaimedAt(timestamptz) | (UserId, Kind, ClaimedAt) | `AdRewardRow.cs` |
| `matches` | Id(uuid PK), UserId(uuid), Stake(int), PlayerCount(int), Status(text), CreatedAt, SettledAt(null) | (UserId) | `Domain/Matches/Match.cs` |
| `games` | Id(uuid PK), RoomCode(text), Stake(int), TargetPlayers(int), StartedAtUtc, EndedAtUtc(null), WinnerSeats(text null) | 없음 | `GameHistoryRows.cs:9-25` |
| `game_players` | Id(bigint identity PK), GameId(uuid), Seat(int), UserId(uuid null), Nickname(text), IsBot(bool), FinalDebt(int null), Payout(bigint) | (GameId), (UserId) | `GameHistoryRows.cs:31-48` |
| `game_events` | Id(bigint identity PK), GameId(uuid), RoundIndex(int), Seat(int null), Type(text), DataJson(jsonb), AtUtc | (GameId, Id) | `GameHistoryRows.cs:53-69` |

### 여기서 바로 눈에 띄는 것

**외래키가 하나도 없습니다.** `ledger.UserId`, `game_players.UserId`, `game_players.GameId`,
`matches.UserId` 전부 참조 제약 없이 떠 있습니다. EF가 탐색 속성 없이 매핑돼 있어서 자동 생성도
안 됐습니다. 지금은 코드가 올바르게 쓰고 있어서 문제가 안 드러나지만, 계정 삭제 경로(M5)를 만드는
순간 고아 행을 막을 장치가 없습니다.

**`ledger`에 시각이 없습니다.** `LedgerRow.cs`는 Id, UserId, Delta, Reason 넷뿐이고 최초
마이그레이션(`20260615043732_InitialCreate.cs`) 이후 컬럼이 추가된 적이 없습니다. 시간 정보는 Id의
증가 순서로만 유추할 수 있습니다. 이건 "어제 하루 유입된 포인트가 얼마인가" 같은 가장 기본적인 경제
질문에도 답할 수 없다는 뜻이고, `considerations.md` R4가 요구하는 "환불·분쟁 대응 원장"의 전제가
성립하지 않는다는 뜻입니다.

**`ledger`에 상관관계 참조가 없습니다.** `StakeEscrow` 행을 보고 그것이 어떤 방의 입장료였는지 알 수
없습니다. `IStakeBank`의 시그니처 자체가 `PayoutAsync(Guid userId, long amount)`라 게임 id를 받지
않습니다(`Realtime/IStakeBank.cs`). 그래서 `game_players.Payout`(지급했어야 할 금액)과 실제 원장
기록을 대조할 방법이 없습니다.

이게 실제 사고로 이어질 수 있는 지점이 하나 있습니다. `Room.cs:555`가 배당을
`_ = _bank.PayoutAsync(userId, share)`로 던지고 곧바로 `CloseRoom`으로 넘어갑니다. 이 쓰기가 실패하면
우승자는 상금을 못 받고 그 사실이 어디에도 남지 않습니다. `Room.cs:622`의 환불도 같습니다. 원장에
gameId를 넣어두면 최소한 "`game_players.Payout > 0`인데 대응하는 원장 행이 없는 좌석"을 쿼리로 찾아
사후 보정할 수 있습니다. 스키마 하나로 운영 사고 하나를 덮는 셈이라 비용 대비 효과가 큽니다.

**잔액 계산이 전량 로드입니다.** `EfLedgerStore.LoadWalletAsync`는 해당 유저의 원장 행을 전부
`ToListAsync()`로 끌어와 `Wallet.Rehydrate`에 넘기고, `Wallet.Balance`가 C#에서 `Sum`을 돕니다
(`EfLedgerStore.cs:24-33`, `Wallet.cs:29`). SQL의 `SUM`이 아니라 애플리케이션 메모리 합산입니다. 게다가
이 호출이 `/me`, 광고 보상, 매치 시작, 판돈 방 입장·정산의 모든 경로에 있고, 상당수는
`WithWalletLockAsync`가 잡은 advisory lock 안에서 실행됩니다. 즉 한 유저의 원장이 길어질수록 그 유저의
모든 요청이 느려지고, 락 보유 시간도 같이 늘어납니다.

**`Purchase`와 `DailyGrant` 사유가 정의돼 있지만 쓰이지 않습니다.** `LedgerReason` enum에는 있는데
(`Domain/Wallet/LedgerReason.cs`), 서버 전체를 grep한 결과 이 둘을 쓰는 코드는 없습니다. 실제로 쓰이는
사유는 Welcome, AdReward, BankruptcyAid, StakeEscrow, StakePayout, StakeRefund 여섯입니다.

**싱글과 멀티의 기록이 서로 다른 테이블에 있습니다.** REST의 `/match/start`는 `matches`에만 쓰고
(`MatchService.cs`), 실시간 방은 `games`/`game_players`/`game_events`에만 씁니다
(`EfGameHistoryStore.cs`). 두 테이블 사이에 연결이 없습니다.

**게임 모드 구분이 없습니다.** `RoomRegistry.Create`(친구방)와 `RoomRegistry.QuickMatch`(맞춤게임)가
만드는 방이 DB에서 구별되지 않습니다. `Room`에 모드 필드가 없고 `games`에도 컬럼이 없어서, 저장되는
것은 6자리 `RoomCode`뿐입니다. 게다가 `FindByCode`가 모든 방을 대상으로 동작하므로 맞춤매칭으로 생긴
방에 친구가 코드로 들어올 수도 있어, 런타임에서조차 모드가 깔끔하게 나뉘지 않습니다. R2가 권고하는
"랭킹을 판돈 결과와 분리"를 하려면 이 구분이 먼저 필요합니다.

---

## 2. 설계 원칙

과잉 설계를 피하기 위해 판단 기준을 먼저 적어 둡니다. 유저가 아직 없는 서비스라 대부분의 확장은
"나중에"가 정답이고, 지금 해야 하는 것은 **나중에 하면 데이터를 복원할 수 없는 것들**뿐입니다.

지금 넣어야 하는 것은 세 종류입니다. 첫째, 기록하지 않으면 영영 사라지는 사실(변동 시각, 인과 관계).
둘째, 나중에 넣으면 기존 데이터의 값을 확정할 수 없는 것(유상·무상 구분. 지금은 전부 무상이라 백필이
자명하지만, IAP가 섞인 뒤에는 과거 행의 분류가 애매해집니다). 셋째, 붙이는 비용이 지금 가장 싼 것
(외래키. 행이 적을 때 검증이 순간입니다).

반대로 미루는 것은 집계 테이블, 파티셔닝, 별도 잔액 스냅샷 테이블, CS 티켓 관리, 관리자 콘솔용
스키마입니다. 전부 규모가 생긴 뒤에 만들어도 손실이 없고, 지금 만들면 검증되지 않은 복잡도만 남습니다.
`mobile-architecture.md` §9의 판단과 같은 기준입니다.

원장에 대한 원칙은 유지합니다. 잔액의 진실은 원장이고(`architecture.md` §5), 어떤 집계 컬럼도 캐시일
뿐입니다. 다만 아래에서 제안하는 `BalanceAfter`는 별도 캐시 테이블이 아니라 원장 행 자체에 기록되는
값이라 원장이 진실이라는 성질을 깨지 않습니다.

---

## 3. `ledger` 재설계 (1순위)

### 3.1 추가할 컬럼

| 컬럼 | 타입 | 널 | 기본값 | 설명 |
|---|---|---|---|---|
| `OccurredAt` | timestamptz | 예 | `now()` | 변동 시각. 기존 행은 NULL(복원 불가라 추정값을 넣지 않습니다) |
| `Kind` | text | 아니오 | `'Free'` | 재화 종류. `Free` / `Paid` |
| `RefType` | text | 예 | | 원인 종류. `Match` / `Game` / `Purchase` / `AdReward` / `Admin` |
| `RefId` | uuid | 예 | | 원인 행의 id. `matches.Id`, `games.Id`, `purchases.Id`가 전부 uuid라 타입이 맞습니다 |
| `BalanceAfter` | bigint | 예 | | 이 행 반영 후의 총 잔액 |

`OccurredAt`을 NOT NULL로 두지 않는 이유는 기존 행에 넣을 정직한 값이 없기 때문입니다. 마이그레이션
시각을 채우면 "2026년 6월의 가입 지급이 8월에 발생한 것"으로 기록되어 나중에 더 큰 혼란을 만듭니다.
NULL을 "기록 이전"으로 읽는 편이 낫고, 신규 행에는 DB 기본값이 들어가므로 애플리케이션 실수로 비는
일도 없습니다.

`RefId`에 외래키는 걸지 않습니다. 다형 참조라 제약을 걸 대상이 하나로 정해지지 않습니다. 대신
`RefType`으로 어느 테이블인지 구분하고, 정합성은 아래 대조 쿼리로 사후 점검합니다.

`AdReward`는 `RefType`만 기록하고 `RefId`는 비웁니다. `ad_rewards.Id`가 bigint라 타입이 맞지 않고,
필요하면 `(UserId, ClaimedAt)`으로 찾을 수 있어 굳이 PK 타입을 바꿀 이유가 없습니다.

### 3.2 잔액 조회 성능

앞서 적었듯 지금은 잔액 한 번 읽는 데 그 유저의 원장 전체가 메모리로 올라옵니다. 해법은 두 단계로
나뉘고, 둘 다 지금 하는 것을 권합니다.

첫 단계는 코드 쪽입니다. 읽기 전용 경로(`/me`)와 검증 경로(차감 전 잔액 확인)는 잔액 숫자만 있으면
되므로, `ILedgerStore`에 `GetBalanceAsync`를 추가해 `SELECT SUM("Delta")`로 내려보내면 됩니다. 스키마
변경 없이 전량 materialize가 사라집니다.

두 번째가 `BalanceAfter`입니다. 이 컬럼이 있으면 잔액 조회가
`SELECT "BalanceAfter" FROM ledger WHERE "UserId" = $1 ORDER BY "Id" DESC LIMIT 1`로 바뀌어 행 수와
무관하게 인덱스 한 번 탐색이 됩니다. 정합성은 이미 확보돼 있는데, 모든 쓰기가
`WithWalletLockAsync`의 advisory lock 안에서 직렬화되기 때문입니다(`EfLedgerStore.cs:39-51`). 락 밖에서
원장에 쓰는 경로가 생기면 이 값이 깨지므로, 그 점만 코드 리뷰 기준으로 지켜야 합니다.

별도 `balances` 스냅샷 테이블은 만들지 않습니다. 테이블이 하나 더 생기면 원장과 어긋날 수 있는 지점이
하나 더 생기는데, 원장 행에 붙은 값은 같은 트랜잭션에서 같이 쓰이므로 어긋날 수 없습니다. 감사 관점에서도
"이 행 시점의 잔액"이 남아 분쟁 대응에 그대로 쓰입니다.

도입 시점을 지금으로 잡는 이유는 백필 비용 때문입니다. 지금은 윈도 함수 한 번으로 끝납니다.

```sql
UPDATE ledger l
SET "BalanceAfter" = c.running
FROM (
  SELECT "Id", SUM("Delta") OVER (PARTITION BY "UserId" ORDER BY "Id") AS running
  FROM ledger
) c
WHERE l."Id" = c."Id";
```

행이 수천 건 규모라면 체감되지 않는 시간입니다. 이걸 유저가 붙은 뒤로 미루면 같은 작업이 전체 테이블
잠금을 동반한 대량 UPDATE가 됩니다.

### 3.3 유상·무상 구분

한국 게임 환경에서 유상 재화와 무상 재화의 구분은 환불 산정의 전제입니다. 정확한 법적 요건은
`considerations.md` R1/R4의 법무 트랙에서 확정해야 하지만(확인 필요), 스키마 관점에서 필요한 것은
분명합니다. 유상 포인트가 얼마나 남아 있는지, 그리고 구매한 포인트 중 얼마가 이미 소진됐는지를 답할 수
있어야 합니다.

`Kind` 컬럼이 그 답의 최소 단위입니다. 유상 잔액은 `SUM("Delta") WHERE "Kind" = 'Paid'`가 됩니다.

여기서 따라오는 설계 결정이 하나 있습니다. 차감할 때 어느 쪽부터 소진하는가입니다. 무상 우선 소진이
일반적이고 유저에게도 유리합니다(유상 잔액이 남아 있어야 환불 가능 금액이 큽니다). 이 정책을 택하면
차감 한 건이 원장 두 행이 될 수 있습니다. 1,000P를 쓰는데 무상이 600P뿐이면 무상 -600, 유상 -400 두 행을
씁니다. 지금 `Wallet.Debit`은 한 건만 반환하므로(`Wallet.cs:44-52`) 도메인 변경이 필요합니다.

다만 그 구현은 지금 하지 않습니다. 유상 포인트가 존재하지 않는 동안에는 분기가 항상 무상 100%라
실행되지 않는 코드만 남습니다. 지금은 `Kind` 컬럼만 넣고 전부 `Free`로 두고, 분할 차감은 IAP를 붙이는
M6에 같이 구현합니다. 컬럼을 미리 넣는 이유는 앞서 적은 대로 나중에는 과거 행의 분류가 애매해지기
때문입니다.

### 3.4 `LedgerReason` 확장

IAP 시점에 필요한 사유입니다. 지금 추가하지 않아도 되지만 설계상 자리를 잡아 둡니다.

| 사유 | 부호 | 용도 |
|---|---|---|
| `Purchase` | + | IAP 지급. enum에 이미 있고 미사용 |
| `PurchaseRefund` | − | 환불·취소에 따른 회수 |
| `AdminGrant` | + | CS 보상 지급 |
| `AdminRevoke` | − | 어뷰징 적발 시 회수 |

`AdminGrant`/`AdminRevoke`는 CS를 시작하는 시점에 반드시 생깁니다. 운영자가 psql로 원장에 직접 행을
넣게 되는데, 그때 사유가 없으면 기존 사유 중 아무거나 골라 쓰게 되고 그 순간 경제 지표가 오염됩니다.

### 3.5 회수 시 음수 잔액

환불로 포인트를 회수해야 하는데 유저가 이미 다 써버린 경우가 실제로 발생합니다. `Wallet.Debit`은 잔액
부족이면 예외를 던지므로(`Wallet.cs:47-50`) 이 경로로는 회수가 불가능합니다.

권고는 회수 전액을 원장에 기록해 잔액이 음수가 되는 것을 허용하고, 음수 잔액 계정은 게임 입장을 막는
쪽입니다. 원장이 진실이라는 원칙을 지키면서 회계도 맞습니다. 회수 가능한 만큼만 회수하면 원장 합계와
실제 지급액이 어긋나 나중에 대사가 불가능해집니다. 도메인에는 `Wallet.Debit`을 우회하는 별도 메서드
(예: `Revoke`)가 필요하고, 잔액 검사는 게임 입장 지점에서 합니다.

---

## 4. `accounts` 확장

### 4.1 계정 본체에 추가할 컬럼

| 컬럼 | 타입 | 널 | 기본값 | 시점 | 설명 |
|---|---|---|---|---|---|
| `Status` | text | 아니오 | `'Active'` | 지금 | `Active` / `Suspended` / `Deleted`. 현재 상태 캐시이고 이력은 `account_sanctions` |
| `LastLoginAt` | timestamptz | 예 | | 지금 | 휴면·이탈 분석과 CS 1차 확인. 로그인 성공 시 갱신 |
| `DeletionRequestedAt` | timestamptz | 예 | | 지금 | 탈퇴 요청 시각. 스토어 정책상 필수 경로 |
| `DeletedAt` | timestamptz | 예 | | 지금 | 익명화 완료 시각 |
| `GuestSecretHash` | text | 예 | | 지금 | 게스트 재로그인 자격. `mobile-architecture.md` §2가 이미 권고한 컬럼 |
| `SocialLinkedAt` | timestamptz | 예 | | 지금 | 소셜 연동 시각. 4.2를 적용하면 `account_socials.LinkedAt`으로 대체 |
| `CountryCode` | char(2) | 예 | | M5 | 웹보드 규제 한도가 국가별로 갈릴 수 있어 남겨 둡니다(R1) |
| `Language` | text | 예 | | M5 | BCP-47. 다국어 시점에 필요 |
| `LastSeenPlatform` | text | 예 | | M5 | `Android` / `iOS` / `WebGL`. 크래시 대응 시 첫 질문 |
| `LastSeenAppVersion` | text | 예 | | M5 | 버전별 이슈 격리 |
| `LastSeenDeviceId` | text | 예 | | M5 | R2 담합 탐지의 1차 신호 |

시점을 나눈 기준은 "지금 안 넣으면 데이터가 사라지는가"입니다. 상태·탈퇴·마지막 로그인은 사건이
일어나는 순간 기록하지 않으면 복원되지 않습니다. 반면 국가·언어·기기·앱 버전은 다음 로그인 때 다시
알 수 있는 값이고, 게다가 현재 서버는 이 정보를 받지도 않습니다. 클라가 보내는 경로부터 만들어야
해서 스키마만 앞서 넣을 이유가 없습니다.

기기와 IP는 개인정보라 보존 기간을 처리방침에 명시해야 합니다(PIPA, R4). accounts에 마지막 값만
캐시하고 이력은 `login_events`에 두면 보존 기간 관리가 한 테이블에서 끝납니다.

### 4.2 소셜 연동은 별도 테이블로 분리합니다

현재는 `accounts.Provider` / `accounts.SocialSubject` 한 쌍이라 1인 1소셜입니다. 분리를 권고합니다.

근거는 기기 교체 시나리오입니다. Android에서 Google로 가입한 유저가 iPhone으로 갈아타면 Apple 로그인만
쓸 수 있는 상황이 생깁니다. 1소셜 구조에서는 이 유저가 새 계정을 만들게 되고, 포인트와 전적을 잃습니다.
`mobile-architecture.md` §2가 결정한 "자동 병합은 하지 않는다"는 게스트↔소셜 충돌에 대한 판단이고,
같은 사람이 두 provider를 자기 계정에 붙이는 것과는 다른 문제입니다. 후자를 막을 이유는 없습니다.

Apple 심사 규정상 소셜을 하나라도 제공하면 Sign in with Apple을 함께 제공해야 하므로(§2), iOS 출시
시점에는 최소 두 provider가 공존합니다. Kakao까지 붙이면 셋입니다. provider가 늘어날수록 1소셜 제약의
비용이 커집니다.

지금 분리하는 것이 가장 쌉니다. 운영 소셜 검증기가 `NotConfiguredSocialVerifier`라 항상 예외를 던지므로
(`Program.cs`의 DI 등록, `BBONG_SOCIAL_DEV_BYPASS`가 켜져 있지 않으면), 운영 DB에 `Provider IS NOT NULL`인
계정은 0건일 가능성이 높습니다. 그렇다면 이관할 데이터가 없어 마이그레이션이 사실상 공짜입니다.
**확인 필요:** `SELECT count(*) FROM accounts WHERE "Provider" IS NOT NULL`.

**`account_socials`**

| 컬럼 | 타입 | 널 | 기본값 | 설명 |
|---|---|---|---|---|
| `Id` | bigint identity | 아니오 | | PK |
| `UserId` | uuid | 아니오 | | FK → `accounts.Id` |
| `Provider` | text | 아니오 | | `Google` / `Apple` / `Kakao` |
| `Subject` | text | 아니오 | | provider가 준 고유 식별자 |
| `LinkedAt` | timestamptz | 아니오 | `now()` | 연동 시각 |
| `UnlinkedAt` | timestamptz | 예 | | 연동 해제 시각. 행은 지우지 않고 이력으로 남깁니다 |

인덱스는 셋입니다. `(Provider, Subject) UNIQUE WHERE "UnlinkedAt" IS NULL`이 "한 소셜 계정은 한 유저"를
보장하고(현재 accounts의 부분 유니크 인덱스를 그대로 옮긴 것입니다), `(UserId, Provider) UNIQUE WHERE
"UnlinkedAt" IS NULL`이 "한 계정에 같은 provider 둘"을 막고, `(UserId)`가 조회용입니다.

해제 이력을 남기는 이유는 계정 탈취 CS 때문입니다. "내 계정에 연결된 구글이 바뀌었어요"라는 문의는
연동/해제 시각이 없으면 조사할 수 없습니다.

### 4.3 닉네임 변경 이력

`/me/nickname`이 이미 열려 있고(`Program.cs`), `UserAccount.Rename`이 값을 덮어씁니다. 이전 닉네임은
남지 않습니다. 욕설·사칭 신고 대응과 "저 사람이 이름 바꾸고 또 왔어요" 유형의 문의에는 이력이 필요합니다.

**`nickname_history`**: `Id`(bigint PK), `UserId`(uuid, FK), `OldNickname`(varchar 12),
`NewNickname`(varchar 12), `ChangedAt`(timestamptz). 인덱스는 `(UserId, ChangedAt DESC)`.

시점은 M5입니다. 유저가 없는 동안 신고가 들어올 일이 없고, 변경 횟수 제한 같은 정책이 생기면 그때 같이
붙이는 편이 자연스럽습니다. 다만 CS를 시작하기 전에는 반드시 들어가야 합니다.

### 4.4 탈퇴 처리 방식

Play와 App Store 모두 앱 내 삭제 요청 경로를 요구하므로 출시 필수 항목입니다
(`mobile-architecture.md` §6 5순위).

원장을 지우면 안 됩니다. 회계와 분쟁 대응의 근거이고, 삭제하면 그 유저와 함께 게임한 다른 사람들의
정산 근거까지 흔들립니다. 그래서 계정 행을 tombstone으로 만드는 익명화가 맞습니다.

절차는 요청 시각(`DeletionRequestedAt`)을 기록하고 유예 기간(7일 정도)을 둔 뒤 배치가 익명화하는
흐름입니다. 유예를 두는 이유는 충동적 탈퇴 취소와, 탈퇴 직전 어뷰징에 대한 조사 시간 확보입니다.
익명화에서는 `Nickname`을 고정 문자열로, `account_socials`의 `Subject`를 제거(연동 해제 처리),
`GuestSecretHash`를 NULL로, `Status`를 `Deleted`로, `DeletedAt`을 기록합니다. 원장에는 UserId만 남습니다.

`game_players.Nickname`은 게임 시점 스냅샷이라 탈퇴 후에도 남습니다. 이걸 같이 지우면 다른 참가자의
전적 화면이 깨집니다. 권고는 데이터를 그대로 두고 조회 시점에 `accounts.Status`를 보고 표시명을 바꾸는
쪽입니다. 스냅샷을 훼손하지 않으면서 노출은 막을 수 있습니다.

---

## 5. 로그인 세션 이력

"어디까지 남길 것인가"가 이 항목의 전부입니다. 과하면 비용이고 없으면 CS를 못 합니다.

기록 대상을 **새 자격 발급 시점으로 한정**할 것을 권합니다. 구체적으로 `/auth/guest`, `/auth/social`,
`/auth/link`, 그리고 §2가 도입하기로 한 `/auth/guest/resume` 호출입니다. 액세스 토큰 갱신마다 남기면
토큰 수명이 60분이라 유저당 하루 최대 24행이 쌓이는데, 그 행들이 알려주는 것은 "앱을 계속 켜뒀다"는
사실뿐이라 값이 없습니다. 자격 발급 시점만 남기면 유저당 하루 몇 행이고, CS가 실제로 필요로 하는
"이 계정에 언제 누가 들어왔나"는 그대로 답할 수 있습니다.

실패도 남깁니다. 성공만 남기면 계정 탈취 시도를 볼 수 없습니다.

**`login_events`**

| 컬럼 | 타입 | 널 | 설명 |
|---|---|---|---|
| `Id` | bigint identity | 아니오 | PK |
| `UserId` | uuid | 예 | 실패 시 대상 미상일 수 있어 nullable |
| `Kind` | text | 아니오 | `Guest` / `Social` / `Resume` / `Link` |
| `Provider` | text | 예 | 소셜인 경우 |
| `Result` | text | 아니오 | `Success` / `Failed` |
| `FailureReason` | text | 예 | 토큰 검증 실패, 이미 연동됨 등 |
| `Ip` | inet | 예 | 담합·탈취 조사(R2). 개인정보라 보존 기간 관리 대상 |
| `DeviceId` | text | 예 | 클라가 보내는 경우에만 |
| `Platform` | text | 예 | `Android` / `iOS` / `WebGL` |
| `AppVersion` | text | 예 | |
| `AtUtc` | timestamptz | 아니오 | 기본값 `now()` |

인덱스는 `(UserId, AtUtc DESC)`와 `(Ip, AtUtc DESC)` 둘입니다. 후자가 R2 담합 탐지의 출발점입니다.
같은 IP에서 여러 계정이 로그인하는 패턴을 이 인덱스 하나로 찾을 수 있습니다.

전제가 하나 있습니다. **서버가 지금 클라이언트 IP를 전혀 읽지 않습니다.** `RemoteIpAddress`나
`X-Forwarded-For`를 참조하는 코드가 서버 전체에 없습니다. fly.io 프록시 뒤에서 돌기 때문에
`ForwardedHeaders` 미들웨어를 먼저 넣지 않으면 프록시 IP만 기록됩니다. `login_events`를 만들 때 같이
처리해야 하는 작업입니다.

시점은 M5입니다. 소셜 로그인이 실제로 동작하고 CS 창구가 열리는 시점과 맞춥니다.

---

## 6. IAP

### 6.1 `purchases`

| 컬럼 | 타입 | 널 | 기본값 | 설명 |
|---|---|---|---|---|
| `Id` | uuid | 아니오 | | PK. `ledger.RefId`가 가리키는 값 |
| `UserId` | uuid | 아니오 | | FK → `accounts.Id` |
| `Store` | text | 아니오 | | `GooglePlay` / `AppStore` |
| `ProductId` | text | 아니오 | | 스토어 상품 id |
| `PurchaseToken` | text | 아니오 | | Google `purchaseToken`, Apple `transactionId` |
| `OrderId` | text | 예 | | Google `orderId`, Apple `originalTransactionId`. CS가 유저 영수증과 대조하는 값 |
| `Status` | text | 아니오 | `'Pending'` | 6.2의 상태 |
| `PointsGranted` | bigint | 예 | | 실제 지급 포인트. 상품 정의가 바뀌어도 당시 지급액이 남습니다 |
| `PriceAmountMicros` | bigint | 예 | | 스토어가 알려준 결제 금액 |
| `PriceCurrency` | text | 예 | | ISO 4217 |
| `CreatedAt` | timestamptz | 아니오 | `now()` | 클라가 구매를 보고한 시각 |
| `VerifiedAt` | timestamptz | 예 | | 스토어 검증 성공 시각 |
| `GrantedAt` | timestamptz | 예 | | 원장 적립 시각 |
| `RefundedAt` | timestamptz | 예 | | 환불 확인 시각 |
| `FailureReason` | text | 예 | | 검증 실패 사유 |
| `RawReceiptJson` | jsonb | 예 | | 스토어 응답 원문. 분쟁 시 유일한 객관 근거 |

인덱스는 셋입니다.

`(Store, PurchaseToken) UNIQUE`가 **중복 지급 방지의 본체**입니다. 애플리케이션 레벨 중복 검사는
동시 요청에서 뚫리지만 유니크 제약은 뚫리지 않습니다. 클라가 네트워크 실패로 같은 영수증을 두 번
보내도 두 번째 INSERT가 제약 위반으로 떨어지고, 서버는 그걸 잡아 기존 결과를 그대로 반환하면 됩니다.
멱등성을 애플리케이션 로직이 아니라 DB 제약으로 확보하는 형태라 리뷰에서 놓칠 여지가 없습니다.

`(UserId, CreatedAt DESC)`는 CS와 유저 구매 내역 화면용입니다.

`(Status) WHERE "Status" IN ('Pending', 'Verified')`는 미완료 건 정리 배치용 부분 인덱스입니다. 검증은
됐는데 지급 직전에 서버가 죽는 경우가 실제로 생기고, 이 인덱스로 찾아 재처리합니다.

### 6.2 상태 전이

```
Pending ──검증 성공──> Verified ──원장 적립──> Granted
   │                                              │
   └──검증 실패──> Failed              스토어 환불 통지
                                                  ↓
                                              Refunded ──회수 완료──> Revoked
```

`Verified`와 `Granted`를 나누는 이유는 그 사이가 실패 지점이기 때문입니다. 검증에 성공했는데 원장
적립이 실패하면 유저는 돈을 냈는데 포인트를 못 받은 상태가 됩니다. 상태를 나눠 두면 배치가 정확히 그
행들만 골라 재처리할 수 있습니다. 한 상태로 뭉치면 재처리 대상을 특정할 수 없습니다.

`Refunded`와 `Revoked`를 나누는 것도 같은 이유입니다. 환불 통지 수신과 포인트 회수는 별개 작업이고,
회수 시점에 잔액이 모자라면(3.5) 처리가 지연될 수 있습니다.

원장 연결은 `ledger.RefType = 'Purchase'`, `ledger.RefId = purchases.Id`, `Kind = 'Paid'`입니다. 환불
회수도 같은 `RefId`를 쓰고 `Reason = 'PurchaseRefund'`, `Delta`는 음수입니다. 이렇게 하면 구매 한 건에
달린 지급과 회수가 한 쿼리로 묶입니다.

### 6.3 환불 통지 수신

Google Play는 RTDN(Real-time Developer Notifications)을 Pub/Sub으로, App Store는 Server Notifications V2를
웹훅으로 보냅니다. 둘 다 수신 엔드포인트가 필요하고, 이건 스키마가 아니라 M6의 구현 항목입니다.
스키마 관점에서 필요한 건 위 상태 전이를 표현할 수 있다는 것뿐입니다.

미성년자 환불 대응에서 실제로 물어보게 되는 질문은 "구매한 포인트가 얼마나 소진됐는가"입니다.
`Kind = 'Paid'`인 원장 행들의 합이 그 답이고, 그래서 3.3의 유상·무상 구분이 IAP보다 먼저 들어가야 합니다.

### 6.4 시점

`purchases` 테이블 자체는 M6입니다. 지금 만들어 봐야 쓸 코드가 없습니다. 반면 `ledger.Kind`,
`RefType`, `RefId`는 지금 넣습니다. 3장의 이유와 같습니다.

---

## 7. 게임 기록

프로필 화면의 전적이 `ProfileBootstrap.cs:39`에 `"전적\n0전 0승 0패"`로 하드코딩돼 있습니다. 이걸
채우려면 무엇이 필요한지부터 봅니다.

### 7.1 전적 집계의 걸림돌

승패 판정이 문자열 파싱입니다. `games.WinnerSeats`가 `"0,2"` 형태의 CSV이고
(`GameHistoryRows.cs:24`), 내가 이겼는지 알려면 이 문자열을 파싱해 내 좌석 번호가 있는지 봐야 합니다.
Postgres에서 못 할 일은 아니지만 인덱스를 못 타고 쿼리가 지저분해집니다.

권고는 `game_players.Won`(boolean, NOT NULL, 기본 false) 추가입니다. 백필은 기존 CSV로 계산할 수 있습니다.

```sql
UPDATE game_players p
SET "Won" = true
FROM games g
WHERE g."Id" = p."GameId"
  AND g."WinnerSeats" IS NOT NULL
  AND p."Seat"::text = ANY(string_to_array(g."WinnerSeats", ','));
```

이걸 넣으면 프로필 전적이 다음 쿼리 하나가 됩니다. 기존 `(UserId)` 인덱스를 그대로 씁니다.

```sql
SELECT count(*) AS played, count(*) FILTER (WHERE "Won") AS won
FROM game_players WHERE "UserId" = $1;
```

### 7.2 봇전을 전적에 넣을 것인가

넣지 않는 것을 권합니다. 싱글 봇전은 `matches`에만 기록되고 `games`에는 흔적이 없어서, 넣으려면 두
테이블을 UNION 해야 합니다. 그리고 봇전은 연습 성격이라 전적에 섞으면 지표의 의미가 흐려집니다.
전적은 `game_players` 기준(사람이 참여한 실시간 게임)으로 정의하고, 화면에도 그렇게 표기하면 됩니다.

다만 `game_players`에는 봇 좌석도 들어 있으므로(`IsBot`), 상대 전적 같은 지표를 뽑을 때는 필터가
필요합니다. 본인 전적은 `UserId`가 NULL이 아닌 행만 조회하므로 자연히 걸러집니다.

### 7.3 추가할 컬럼

| 테이블 | 컬럼 | 타입 | 시점 | 이유 |
|---|---|---|---|---|
| `game_players` | `Won` | boolean NOT NULL DEFAULT false | 지금 | 7.1 |
| `games` | `Mode` | text | M5 | `Friend` / `QuickMatch`. R2가 요구하는 "랭킹과 판돈 결과 분리"의 전제 |
| `games` | `EndReason` | text | M5 | `Completed` / `Aborted`. 서버 재시작으로 죽은 판을 전적에서 빼려면 필요 |
| `game_players` | `LeftAtUtc` | timestamptz | M5 | 이탈 시각. 이탈=패배 정책의 근거이자 담합 탐지 신호 |
| `game_players` | `ReplacedByBot` | boolean | M5 | 봇 대체 여부 |

`games.Mode`는 스키마만으로 끝나지 않습니다. 지금 `Room`에 모드 필드가 없고
`RoomRegistry.Create`와 `RoomRegistry.QuickMatch`가 같은 종류의 방을 만들기 때문에, 서버 쪽에 구분을
먼저 넣어야 합니다. 게다가 `FindByCode`가 맞춤매칭 방도 코드로 찾아 주므로 맞춤매칭 방에 친구가
들어오는 것이 가능합니다. `Mode`를 "생성 경로"로 정의할지 "실제 참가 구성"으로 정의할지 판단이 필요하고,
R2 대응 목적이라면 후자가 맞습니다. 이건 스키마보다 큰 결정이라 M5에서 다룹니다.

### 7.4 집계 테이블은 만들지 않습니다

`user_stats` 같은 집계 테이블은 지금 필요 없습니다. 유저 한 명의 게임 수가 수천 건이 되어도 `(UserId)`
인덱스를 탄 count는 밀리초 단위입니다. 도입 판단 기준을 숫자로 적어 두면, 프로필 전적 쿼리가 p95에서
50ms를 넘거나 유저당 `game_players` 행이 5만 건을 넘을 때입니다. 그 전에 만들면 원본과 집계가 어긋나는
버그만 얻습니다.

랭킹은 다릅니다. 전체 유저를 정렬해야 하므로 그 시점에는 집계가 필요합니다. 다만 랭킹은 아직 로드맵에
없고(프로필 전적만 있습니다), R2 때문에 설계 자체가 열려 있어 지금 정하지 않습니다.

---

## 8. 운영과 CS

### 8.1 제재 이력

`accounts.Status`는 현재 상태만 담습니다. 왜 정지됐는지, 언제까지인지, 누가 했는지는 별도 이력이
필요합니다. 제재는 분쟁이 붙는 영역이라 근거를 남기지 않으면 대응이 불가능합니다.

**`account_sanctions`**

| 컬럼 | 타입 | 널 | 설명 |
|---|---|---|---|
| `Id` | bigint identity | 아니오 | PK |
| `UserId` | uuid | 아니오 | FK → `accounts.Id` |
| `Kind` | text | 아니오 | `Warn` / `Suspend` / `Ban` / `PointRevoke` |
| `Reason` | text | 아니오 | 사람이 읽는 사유 |
| `EvidenceJson` | jsonb | 예 | 탐지 쿼리 결과나 신고 내용 원문 |
| `StartsAt` | timestamptz | 아니오 | |
| `EndsAt` | timestamptz | 예 | NULL이면 영구 |
| `IssuedBy` | text | 아니오 | 운영자 식별자 |
| `CreatedAt` | timestamptz | 아니오 | 기본값 `now()` |

인덱스는 `(UserId, CreatedAt DESC)`와 `(EndsAt) WHERE "EndsAt" IS NOT NULL`입니다. 후자는 정지 해제
배치가 씁니다.

`PointRevoke`는 `ledger`의 `AdminRevoke` 행과 짝을 이룹니다. 제재 기록에 회수 사유가 있고 원장에 회수
금액이 있는 형태입니다.

### 8.2 문의 대응

CS 티켓 테이블은 만들지 않습니다. 이메일이나 외부 채널 도구로 시작하고, DB에는 문의를 받았을 때
조회할 데이터만 있으면 됩니다. `mobile-architecture.md` §6이 같은 판단을 했고, 그쪽이 지적한 실제 병목은
스키마가 아니라 조회 권한입니다. `/games/{gameId}/events`가 본인 참여 게임만 허용해서 운영자가 볼 수
없습니다(`Program.cs`의 `participated` 검사).

스키마 관점에서 이걸 풀려면 운영자 식별이 필요합니다. 별도 관리자 테이블을 만들 필요는 없고,
`accounts`에 `IsOperator`(boolean, 기본 false)를 붙이거나 JWT 클레임으로 처리하면 됩니다. 운영자가 한
자릿수인 동안은 후자가 더 쌉니다. 판단은 M5에서 합니다.

### 8.3 담합 탐지 (R2)

필요한 데이터는 새 테이블이 아니라 앞서 추가한 것들의 조합입니다.

같은 IP·기기에서 여러 계정이 로그인하는 패턴은 `login_events`의 `(Ip, AtUtc DESC)` 인덱스로 찾습니다.
반복 대전 패턴은 `game_players`를 자기 조인해서 "같은 두 유저가 같은 방에 있었던 횟수"로 계산할 수
있고, 이건 판수가 적은 동안 별도 테이블 없이 충분합니다.

포인트가 한 방향으로만 흐르는 패턴이 가장 직접적인 신호인데, 여기에 `ledger.RefId`가 필요합니다.
게임별로 누가 얼마를 잃고 누가 얼마를 얻었는지를 원장에서 바로 집계할 수 있어야 방향성이 보입니다.
지금은 `StakeEscrow`와 `StakePayout` 행이 어느 게임 것인지 알 수 없어서 이 분석이 아예 불가능합니다.

탐지 결과를 남길 곳은 `account_sanctions.EvidenceJson`이면 됩니다. 별도 탐지 로그 테이블은 탐지가 실제로
자동화된 뒤에 만듭니다.

### 8.4 정산 누락 대조

1장에서 지적한 fire-and-forget 배당 문제는 `ledger.RefId`가 들어가면 아래 쿼리로 감시할 수 있게 됩니다.

```sql
SELECT p."GameId", p."Seat", p."UserId", p."Payout"
FROM game_players p
WHERE p."Payout" > 0 AND p."UserId" IS NOT NULL
  AND NOT EXISTS (
    SELECT 1 FROM ledger l
    WHERE l."UserId" = p."UserId" AND l."RefType" = 'Game' AND l."RefId" = p."GameId"
      AND l."Reason" = 'StakePayout'
  );
```

이걸 주기적으로 돌려 결과가 0이 아니면 알림을 보냅니다. `mobile-architecture.md` §6이 알림 대상으로
꼽은 "정산이 실패했을 때"를 실제로 감지하는 방법이 이것입니다.

---

## 9. 인덱스 전략

### 9.1 추가

| 테이블 | 인덱스 | 이유 |
|---|---|---|
| `ledger` | `("UserId", "Id" DESC)` | 잔액 조회가 최신 행 1건 읽기로 바뀝니다(3.2). 기존 `(UserId)` 단독 인덱스를 대체 |
| `games` | `("StartedAtUtc" DESC)` | 최근 게임 목록. 지금 `games`에 인덱스가 하나도 없습니다 |
| `matches` | `("Status", "CreatedAt") WHERE "Status" = 'InProgress'` | 미정산 매치 타임아웃 정리 잡(M5, R7)이 쓰는 대상 |
| `accounts` | `("DeletionRequestedAt") WHERE "DeletionRequestedAt" IS NOT NULL` | 탈퇴 처리 배치 |
| `account_socials` | 4.2 참조 | |
| `login_events` | `("UserId", "AtUtc" DESC)`, `("Ip", "AtUtc" DESC)` | 5장 |
| `purchases` | `("Store", "PurchaseToken") UNIQUE`, `("UserId", "CreatedAt" DESC)`, `("Status") WHERE ...` | 6.1 |

### 9.2 지금 만들지 않는 것

`ledger`의 `(Reason, OccurredAt)`은 경제 지표용인데, 일별 집계는 하루 한 번 도는 배치라 순차 스캔이어도
문제가 없습니다. 테이블이 수백만 행이 된 뒤에 만듭니다.

`game_events`는 `(GameId, Id)` 하나로 충분합니다. 이벤트는 항상 게임 단위로 조회하고 다른 접근 경로가
없습니다.

`accounts.Nickname` 인덱스도 만들지 않습니다. 닉네임에 유니크 제약이 없고 검색 기능도 없습니다.
CS가 닉네임으로 유저를 찾는 상황이 생기면 그때 만듭니다.

### 9.3 외래키

지금 하나도 없습니다. 아래를 추가할 것을 권하고, 행이 적은 지금이 가장 쌉니다.

| 자식 | 부모 | 삭제 동작 |
|---|---|---|
| `ledger.UserId` | `accounts.Id` | RESTRICT (계정은 익명화하지 삭제하지 않습니다) |
| `ad_rewards.UserId` | `accounts.Id` | RESTRICT |
| `matches.UserId` | `accounts.Id` | RESTRICT |
| `game_players.GameId` | `games.Id` | CASCADE |
| `game_events.GameId` | `games.Id` | CASCADE |
| `game_players.UserId` | `accounts.Id` | RESTRICT, nullable(봇 좌석은 NULL) |
| `account_socials.UserId` | `accounts.Id` | CASCADE |
| `purchases.UserId` | `accounts.Id` | RESTRICT |

`game_events`에 CASCADE를 거는 이유는 보존 정책 때문입니다. 오래된 게임을 지울 때 부모 행 하나만
지우면 이벤트가 따라 지워집니다.

추가할 때는 `ADD CONSTRAINT ... NOT VALID`로 먼저 걸고 별도로 `VALIDATE CONSTRAINT`를 실행합니다.
NOT VALID는 기존 행을 검사하지 않아 즉시 끝나고, VALIDATE는 ACCESS EXCLUSIVE가 아닌 약한 락으로 돕니다.
다만 그 전에 고아 행이 없는지 확인해야 합니다. VALIDATE가 실패하면 제약이 NOT VALID 상태로 남습니다.

```sql
SELECT count(*) FROM ledger l
  LEFT JOIN accounts a ON a."Id" = l."UserId" WHERE a."Id" IS NULL;
SELECT count(*) FROM game_players p
  LEFT JOIN games g ON g."Id" = p."GameId" WHERE g."Id" IS NULL;
```

두 번째 쿼리는 실제로 걸릴 가능성이 있습니다. `EfGameHistoryStore.CreateGameAsync`가 게임과 좌석을 한
`SaveChangesAsync`로 쓰기 때문에 정상 경로에서는 안 생기지만, 히스토리 체인이 fire-and-forget이라
(`Room.cs`의 `Chain`) 부분 실패 이력이 있으면 남아 있을 수 있습니다. 확인이 먼저입니다.

---

## 10. 데이터 보존 정책

| 테이블 | 보존 | 근거 |
|---|---|---|
| `ledger` | 영구 | 회계·분쟁 근거. 지우면 잔액의 진실이 사라집니다 |
| `purchases` | 영구 | 거래 기록. 법정 보존 의무 기간은 법무 확인 필요 |
| `accounts` | 영구(익명화) | 원장 참조 무결성 유지 |
| `account_sanctions` | 영구 | 재제재 판단 근거 |
| `games`, `game_players` | 2년 | 전적 표시의 원본 |
| `game_events` | 90일 | 아래 참조 |
| `login_events` | 90일 | 개인정보(IP) 최소 보관 |
| `nickname_history` | 2년 | 신고 대응 |

`game_events`가 유일하게 규모가 문제되는 테이블입니다. `GameSession`을 보면 딜 1건, 턴마다 draw와
discard, 그리고 뽕·족보·라운드 종료가 기록됩니다(`GameSession.cs`의 `output.Log` 호출 10개소).
6인 게임 한 라운드에 30턴이면 draw/discard만 60행이고, 한 세트가 여러 라운드이므로 게임당 수백 행이
될 수 있습니다. **확인 필요:** 운영 DB에서
`SELECT count(*)::float / count(DISTINCT "GameId") FROM game_events`로 실측해야 보존 기간을 숫자로
정당화할 수 있습니다.

90일을 권하는 근거는 이 데이터의 용도입니다. 이벤트 로그는 "그 판에서 무슨 일이 있었나"의 상세이고,
CS 문의는 대개 며칠 안에 들어옵니다. 분쟁의 결론에 해당하는 정보(누가 이겼고 얼마를 받았는지)는
`games`/`game_players`와 원장에 남으므로, 상세 이벤트를 지워도 대응 능력이 크게 줄지 않습니다.

삭제는 배치 DELETE로 시작합니다.

```sql
DELETE FROM game_events WHERE "AtUtc" < now() - interval '90 days';
```

파티셔닝은 하지 않습니다. 월 수천만 행 규모가 되면 DELETE의 vacuum 부담 때문에 월 단위 파티션 + DROP
PARTITION이 유리해지지만, 그 규모가 오기 전에 도입하면 EF Core 매핑과 마이그레이션이 복잡해지기만
합니다. 판단 기준을 숫자로 두면 `game_events`가 3천만 행을 넘거나 삭제 배치가 1분을 넘을 때입니다.

---

## 11. 마이그레이션 계획

### 11.1 전제: 마이그레이션이 서버 기동을 막습니다

`Program.cs`가 부팅 시 `Database.Migrate()`를 실행합니다. fly.toml은 `shared-cpu-1x` / 512MB 머신
1대이고 `--ha=false` 전제라, 마이그레이션이 오래 걸리면 그 시간 동안 서비스가 멈춥니다. 따라서 "무중단"
여부는 단계별로 다음과 같이 판단합니다.

메타데이터만 바꾸는 변경(nullable 컬럼 추가, 상수 기본값이 붙은 컬럼 추가)은 PostgreSQL 11 이후 테이블
재작성 없이 즉시 끝나므로 사실상 무중단입니다. 반면 대량 UPDATE나 CREATE INDEX는 잠금 시간이 행 수에
비례합니다. 지금 규모(계정 수백 건 전제)에서는 전부 초 단위로 끝나므로 배포 지연 몇 초로 흡수됩니다.

정확히 말하면 "무중단"이 아니라 "기동 지연 수 초"입니다. 진짜 무중단이 필요해지면 마이그레이션을 배포와
분리해 별도 잡으로 돌려야 하는데, 지금 규모에서는 과합니다. 다만 유저가 붙은 뒤에 대량 UPDATE를 동반한
마이그레이션을 하려 하면 그때는 분리가 필요하다는 것을 기억해야 합니다. 3.2의 `BalanceAfter` 백필을
지금 하자는 것도 같은 맥락입니다.

### 11.2 단계

**1단계 — 원장 (M3 중, 즉시)**

`ledger`에 `OccurredAt`, `Kind`, `RefType`, `RefId`, `BalanceAfter`를 추가합니다. 전부 nullable이거나
상수 기본값이라 메타데이터 변경입니다. `Kind`는 `DEFAULT 'Free'`로 추가하면 기존 행에도 그 값이 보이므로
별도 백필이 필요 없습니다. `BalanceAfter`만 3.2의 윈도 함수 UPDATE로 백필합니다.
인덱스 `(UserId, Id DESC)`를 만들고 기존 `(UserId)` 인덱스를 제거합니다.

이 단계는 스키마만 바꿔도 기존 코드가 그대로 돕니다. 새 컬럼이 전부 nullable/기본값이라 EF가 INSERT에서
빼도 문제가 없습니다. 코드 변경(§12)은 다음 배포로 나눠도 됩니다.

**2단계 — 계정 (M3~M4)**

`accounts`에 `Status`, `LastLoginAt`, `DeletionRequestedAt`, `DeletedAt`, `GuestSecretHash`를 추가하고,
`account_socials`를 만들어 기존 소셜 연동을 이관합니다.

이관은 expand-contract로 합니다. 새 테이블을 만들고 데이터를 복사한 뒤, `accounts.Provider`와
`SocialSubject`는 **같은 배포에서 지우지 않습니다.** 배포가 롤백되면 구버전 코드가 그 컬럼을 읽기
때문입니다. 다음 배포에서 드롭합니다. 4.2에서 예상한 대로 이관 대상이 0건이면 복사 자체가 no-op입니다.

```sql
INSERT INTO account_socials ("UserId", "Provider", "Subject", "LinkedAt")
SELECT "Id", "Provider", "SocialSubject", "CreatedAt"
FROM accounts WHERE "Provider" IS NOT NULL;
```

`LinkedAt`에 `CreatedAt`을 쓰는 것은 근사입니다. 소셜로 처음 가입한 계정은 정확하고, 게스트에서 승격한
계정은 실제 연동 시각보다 이릅니다. 이관 대상이 0건이면 무의미한 논점입니다.

**3단계 — 외래키와 전적 (M4~M5)**

`game_players.Won` 추가와 7.1의 백필, §9.3의 외래키 일괄 추가(NOT VALID → 사전 점검 → VALIDATE),
`games`와 `matches` 인덱스 추가를 묶습니다. 외래키 추가 전에 고아 행 점검 쿼리를 반드시 먼저 돌립니다.

**4단계 — 운영 (M5)**

`login_events`, `account_sanctions`, `nickname_history` 신설. `accounts`에 국가·언어·기기 컬럼 추가.
`games.Mode` / `EndReason`, `game_players.LeftAtUtc` / `ReplacedByBot` 추가. 전부 신규 테이블이거나
nullable 컬럼이라 즉시 끝납니다.

**5단계 — IAP (M6)**

`purchases` 신설. `ledger`에 `IdempotencyKey`(text, UNIQUE, nullable)와 `PaidBalanceAfter` 추가.
`LedgerReason`에 `PurchaseRefund`, `AdminGrant`, `AdminRevoke` 추가. enum은 문자열로 저장되므로
(`BbongDbContext.cs:50`) DB 변경 없이 값만 늘어납니다.

### 11.3 되돌리기

EF의 `Down` 마이그레이션은 컬럼 드롭이라 데이터가 사라집니다. 1단계를 되돌리면 백필한 `BalanceAfter`가
날아가지만 재계산 가능하므로 손실이 없습니다. 2단계의 `account_socials` 드롭은 이관 후 원본을 남겨
두는 expand-contract 덕분에 안전합니다. 실질적으로 되돌릴 수 없는 것은 원본 컬럼을 드롭하는 배포
이후이므로, 그 배포는 새 코드가 며칠 돌아간 뒤에 합니다.

---

## 12. 코드 변경 지점

파일 단위로 정리합니다. 각 항목이 어느 마이그레이션 단계에 붙는지 같이 적었습니다.

| 파일 | 변경 | 단계 |
|---|---|---|
| `Infrastructure/Persistence/LedgerRow.cs` | 새 컬럼 5개 필드 추가, `From`/`ToEntry` 변환 확장 | 1 |
| `Domain/Wallet/LedgerEntry.cs` | record에 `OccurredAt`, `Kind`, `RefType`, `RefId` 추가. 순수 record라 생성 지점 전부가 영향받습니다 | 1 |
| `Domain/Wallet/Wallet.cs` | `Credit`/`Debit`이 종류와 참조를 받도록. `Balance`가 전량 합산이 아니라 주입된 잔액 기반이 되도록 `Rehydrate` 오버로드 추가 | 1 |
| `Application/ILedgerStore.cs` | `GetBalanceAsync(Guid)` 추가 | 1 |
| `Infrastructure/Persistence/EfLedgerStore.cs` | `LoadWalletAsync`의 전량 로드 제거, `BalanceAfter` 기반 조회, append 시 `BalanceAfter` 계산 | 1 |
| `Infrastructure/InMemory/InMemoryLedgerStore.cs` | 같은 인터페이스 맞춤 | 1 |
| `Application/MatchService.cs` | 에스크로·배당 원장 행에 `RefType='Match'`, `RefId=matchId` 기록 | 1 |
| `Application/LedgerStakeBank.cs` | 같은 목적. 다만 `IStakeBank`가 gameId를 안 받으므로 인터페이스 시그니처부터 바뀝니다 | 1 |
| `Realtime/IStakeBank.cs` | 세 메서드에 gameId 파라미터 추가 | 1 |
| `Realtime/Room.cs` | `PayoutAsync`/`RefundAsync` 호출부(`:555`, `:622`)에 `_gameId` 전달 | 1 |
| `Realtime/WsEndpoint.cs` | `TryEscrowAsync` 호출 3개소(`:112`, `:127`, `:143`). 다만 이 시점엔 아직 gameId가 없어 방 코드로 대신할지 판단 필요 | 1 |
| `Application/ShopService.cs` | 광고 보상 원장 행에 `RefType='AdReward'` | 1 |
| `Application/AccountService.cs` | 가입 지급에 `RefType`, 로그인 시 `LastLoginAt` 갱신, 탈퇴 요청/취소 유스케이스 추가 | 1, 2 |
| `Domain/Accounts/UserAccount.cs` | `Status`, `LastLoginAt`, `DeletionRequestedAt`, `DeletedAt`, `GuestSecretHash`. `LinkSocial`이 단일 값 대입에서 컬렉션 추가로 | 2 |
| `Application/IAccountStore.cs` | `GetBySocialAsync`의 의미가 `account_socials` 조회로 바뀝니다. `TouchLoginAsync` 추가 | 2 |
| `Infrastructure/Persistence/EfAccountStore.cs` | 위 구현. `SaveAsync`의 `SetValues` 방식이 컬렉션 탐색 속성과 어떻게 맞물리는지 확인 필요 | 2 |
| `Infrastructure/InMemory/InMemoryAccountStore.cs` | 같은 인터페이스 맞춤 | 2 |
| `Infrastructure/Persistence/BbongDbContext.cs` | 신규 엔티티 매핑, 인덱스, 외래키 전부 | 1~5 |
| `Program.cs` | `/me` 응답에 전적 추가, 탈퇴 요청 엔드포인트, `ForwardedHeaders` 미들웨어(IP 수집), 운영자 권한 조회 경로 | 2~4 |
| `client/.../ProfileBootstrap.cs:39` | 하드코딩된 `"0전 0승 0패"`를 서버 값으로 | 3 |
| `Infrastructure/Persistence/EfGameHistoryStore.cs` | `CompleteGameAsync`에서 `Won` 기록, 모드·종료 사유 기록 | 3, 4 |
| `Realtime/IGameHistoryStore.cs`, `Realtime/RoomRegistry.cs`, `Realtime/Room.cs` | 방 모드(`Friend`/`QuickMatch`) 전달 경로 | 4 |
| `server/BbongServer.Tests/Infrastructure/EfStoreTests.cs` | 원장 저장소 계약이 바뀌므로 테스트 선행 작성 | 1 |

`LedgerEntry`가 순수 record라 1단계의 파급이 가장 큽니다. 원장에 쓰는 모든 지점이 컴파일 에러로 드러나므로
누락은 없지만, 한 번에 다 고쳐야 빌드가 통과합니다. 컬럼 전부를 nullable로 잡아 둔 것이 여기서도 도움이
되는데, 단계적으로 참조를 채워 넣어도 스키마 제약에 걸리지 않습니다.

---

## 13. 우선순위

### 지금 당장 (M3 안)

1. `ledger`에 `OccurredAt` 추가. 지금 기록하지 않는 시각은 영원히 복원되지 않습니다.
2. `ledger`에 `RefType`/`RefId` 추가. 8.4의 정산 누락 감지와 R2 담합 분석이 여기 걸려 있습니다.
3. `ledger`에 `Kind` 추가. 지금은 전량 `Free`라 백필이 자명하지만, IAP 이후에는 애매해집니다.
4. `ledger`에 `BalanceAfter` 추가와 백필. 지금은 윈도 함수 한 번이고, 나중에는 대량 UPDATE입니다.
5. `accounts`에 `Status`, `LastLoginAt`, `DeletionRequestedAt`, `DeletedAt` 추가. 앞의 셋은 사건 시점에
   기록하지 않으면 사라지고, 탈퇴 경로는 스토어 필수 항목이라 M4 전에 필요합니다.
6. `account_socials` 분리. 이관 대상이 0건일 때가 유일하게 공짜인 시점입니다.
7. `EfLedgerStore.LoadWalletAsync`의 전량 로드 제거. 스키마와 별개로 지금 가장 확실한 성능 개선입니다.

### 다음 (M4~M5)

`game_players.Won`과 백필, 외래키 일괄 추가, `login_events`, `account_sanctions`, `nickname_history`,
`games.Mode`/`EndReason`, `accounts`의 국가·언어·기기 컬럼, `game_events` 보존 배치, `ForwardedHeaders`
설정, 미정산 매치 정리용 부분 인덱스.

### 나중 (M6, IAP와 함께)

`purchases`, 유상·무상 분할 차감 구현, `ledger.IdempotencyKey`와 `PaidBalanceAfter`, 환불 통지 수신,
`LedgerReason` 확장.

### 하지 않는 것

`user_stats` 집계 테이블, `game_events` 파티셔닝, 별도 잔액 스냅샷 테이블, CS 티켓 테이블, 관리자 콘솔용
스키마, 리프레시 토큰 테이블(§9의 판단과 동일), `ledger`의 경제 분석용 인덱스. 전부 규모가 생긴 뒤에
만들어도 손실이 없고, 판단 기준은 각 절에 숫자로 적어 뒀습니다.

---

## 14. 확인 필요

- 운영 DB의 실제 행 수. `accounts`, `ledger`, `games`, `game_players`, `game_events` 각각. 계정 수백 건은
  전달받은 정보이고 직접 확인하지 못했습니다. 11.1의 "초 단위로 끝난다"는 판단이 여기에 걸려 있습니다.
- `SELECT count(*) FROM accounts WHERE "Provider" IS NOT NULL`. 0이면 4.2의 이관이 no-op입니다.
- `game_events`의 게임당 평균 행 수. 10장의 보존 기간을 숫자로 정당화하려면 필요합니다.
- 9.3의 고아 행 점검 두 쿼리. 외래키 추가 전 필수입니다.
- 전자상거래법상 거래 기록 보존 의무 기간. 10장의 `purchases` 영구 보존이 과한지 판단하려면 법무 확인이
  필요합니다(R4).
- 유상·무상 재화 구분의 정확한 법적 요건과 소진 순서 규정. 3.3의 무상 우선 소진은 일반적 관행에 따른
  권고이고 법적 확인은 받지 않았습니다(R1, R4).
- 운영 DB의 백업·PITR 설정. 마이그레이션 실패 시 복구 수단이 무엇인지 확인하지 못했습니다. fly.toml에는
  DB 설정이 없고 `BBONG_DB_CONN`이 시크릿으로 주입됩니다.
- `EfAccountStore.SaveAsync`의 `CurrentValues.SetValues` 방식이 `account_socials` 컬렉션 탐색 속성과
  맞물릴 때의 동작. 2단계에서 실제로 테스트해 봐야 합니다.
