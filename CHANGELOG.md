### 2026-10-01 01:24 — 미사용 테스트 창과 달력 코드 정리

- Changed:
  - `TestWindow.xaml` (삭제)
  - `TestWindow.xaml.cs` (삭제)
  - `Controls/CustomCalendarControl.cs` (삭제)
  - `Converters/DaySummaryConverter.cs` (삭제)
  - `MainWindow.xaml`
  - `CHANGELOG.md`
- Summary:
  - 미사용 소스 4개와 메인 화면의 미사용 변환기 리소스 선언을 제거함.
- Validation:
  - 잔여 참조 검색 및 `git diff --check` 통과.
  - 임시 복사본의 Debug 빌드 성공: 경고 0개, 오류 0개. 검증 임시 디렉터리 제거 완료.
  - 기존 수정 파일과 로그 외 bin 파일의 SHA-256 일치 확인.
- Unresolved:
  - 자동 승인 검토 정책으로 `.vs/`, `obj/`, `SimpleAccountBook.csproj.user`, `bin/Debug/net8.0-windows/Logs/` 정리는 수행하지 못함.

### 2026-10-01 02:13 — 예상 지출·거래 분류·최근 12개월 통계 추가

- Changed:
  - `BudgetWindow.xaml`, `BudgetWindow.xaml.cs`
  - `Models/BudgetModels.cs`, `Models/TransactionRecord.cs`, `Models/AppState.cs`
  - `Services/BudgetService.cs`, `ViewModels/AccountBookViewModel.cs`
  - `MainWindow.xaml`, `MainWindow.xaml.cs`, `SimpleAccountBook.csproj`
  - `Tests/SimpleAccountBook.Tests.csproj`, `Tests/Program.cs`
  - `CHANGELOG.md`
- Summary:
  - 고정·변동 예상 항목의 등록·편집·종료, 반복 일정과 변경 이력, 실제 거래 연결 및 남은 예상액·초과액을 추가함.
  - 기타 실제 지출, 분류·태그 일괄 편집, 검색·필터, 지난달 기준 최근 12개월 월별·분류별·거래처별 통계를 추가함.
  - 등록된 본인 계좌 간 이체만 제외하며, 확인 전 후보는 집계에 유지함. 실제 환불 입금을 원지출에 연결해 전액·부분 환불을 반영함.
  - 기존 달력 집계와 저장·복원에 연결하고 같은 파일 재가져오기 시 사용자 분류·연결 정보를 유지함.
- Validation:
  - Debug 빌드 성공: 경고 0개, 오류 0개. 회귀 검증 12개 시나리오 통과.
  - 예시 엑셀의 141건 중 기존 동작상 0원 1건을 제외한 140건을 가져오며 원본 수입·지출 합계 일치 확인.
  - 기존 저장 파일의 호환성, 실제 파일 저장·복원, 화면 편집 동작 및 밝은/어두운 테마의 4개 탭 렌더링 확인.
  - `git diff --check` 및 원본 엑셀·기존 저장 파일의 SHA-256 보존 확인. 작업 전용 검증 산출물 정리.
- Unresolved:
  - 예시에는 상대 계좌와 환불 식별 정보가 없어 해당 거래는 사용자 지정이 필요함.

### 2026-10-01 02:32 — 사용하지 않는 분류 삭제 추가

- Changed:
  - `BudgetWindow.xaml`
  - `BudgetWindow.xaml.cs`
  - `CHANGELOG.md`
- Summary:
  - 분류 목록에 삭제 버튼을 추가하고 미사용 분류는 확인창 없이 삭제·자동 저장하도록 함.
  - 거래 또는 예상 항목의 이전 기록에서 사용 중인 분류는 삭제를 차단함.
- Validation:
  - XAML 구문·이벤트 연결·사용 여부 검사 정적 확인 및 `git diff --check` 통과.
  - Debug 빌드 성공: 경고 0개, 오류 0개. 별도 임시 검증 파일 없음.
- Unresolved:
  - 없음.

### 2026-10-01 02:59 — 본인 소유 계좌 관리 화면 제거

- Changed:
  - `BudgetWindow.xaml`
  - `BudgetWindow.xaml.cs`
  - `Tests/Program.cs`
  - `CHANGELOG.md`
- Summary:
  - 본인 소유 계좌 목록·입력 폼·관리 버튼과 관련 이벤트 코드를 제거함.
  - 탭 이름을 ‘분류’로 변경하고 분류 관리 화면을 전체 너비로 배치함.
  - 기존 저장 데이터는 유지하고 UI 검증 코드의 삭제된 화면 참조를 제거함.
- Validation:
  - XAML 구문·삭제된 UI 참조 검사 및 `git diff --check` 통과.
  - Debug 빌드 성공: 경고 0개, 오류 0개. 별도 임시 검증 파일 없음.
- Unresolved:
  - 없음.
