# 📊 TÀI LIỆU HƯỚNG DẪN TÍCH HỢP FRONTEND: DASHBOARD RADAR 6+1 VÀ TỔNG QUAN TRẢI NGHIỆM SINH VIÊN

> **Dành cho:** Frontend Developer (Team `fptu-xperience-admin-ui` và `FPT_FE`)  
> **Phiên bản API:** v1  
> **Cổng API Gateway:** `{{VITE_API_BASE_URL}}/api/v1/student-affairs/radar/overview`

---

## 1. 🎯 TỔNG QUAN TÍNH NĂNG & MỤC TIÊU NGHIỆP VỤ

Màn hình **Tổng quan Trải nghiệm Sinh viên (Experience Radar Overview)** cung cấp góc nhìn đo lường toàn diện về chất lượng và độ đa dạng hoạt động trải nghiệm của sinh viên FPT theo mô hình chuẩn **Radar 6+1** (Phụ lục A3 & A4):

- **6 Trục Cốt lõi (Core Six Pillars):**
  1. **Học tập** (`Academic`): Hoạt động học thuật, hội thảo chuyên đề, chứng chỉ chuyên môn, cuộc thi kiến thức.
  2. **Nghiên cứu** (`Research`): Đề tài nghiên cứu khoa học, bài báo công bố, giải thưởng học thuật, lab thực nghiệm.
  3. **Quốc tế** (`Global`): Giao lưu quốc tế, chương trình trao đổi sinh viên, hoạt động ngoại ngữ, trải nghiệm đa văn hóa.
  4. **Thể thao & văn hóa** (`CultureSports`): Rèn luyện thể chất, thi đấu thể thao, hoạt động văn nghệ và phát triển bản thân.
  5. **Cộng đồng** (`Community`): Hoạt động thiện nguyện, phục vụ cộng đồng, trách nhiệm xã hội, phát triển bền vững.
  6. **Khởi nghiệp** (`Entrepreneurship`): Dự án khởi nghiệp, đổi mới sáng tạo, chuyển giao công nghệ, sản phẩm thực tế.
- **Trục Bổ trợ Thực chiến (+1 Pillar):**
  - **Thực chiến dự án (+1)** (`RealWorldWork`): Dự án thực tế tại doanh nghiệp, thực tập on-the-job (OJT), hợp tác doanh nghiệp.

---

## 2. 🔐 QUY TẮC PHÂN QUYỀN & PHẠM VI DỮ LIỆU (CAMPUS SCOPE)

Giao diện Frontend cần phân biệt rõ giữa 2 đối tượng người dùng:

| Vai trò | Quyền hạn xem | Trạng thái Dropdown Chọn Cơ sở |
| :--- | :--- | :--- |
| **Cán bộ CTSV** (`STUDENT_AFFAIRS_ADMIN`) | **Chỉ xem cơ sở của mình** (`actor.CampusCode`). Không được phép truy vấn dữ liệu của cơ sở khác. | **Disable/Read-only** hoặc tự động hiển thị cứng tên cơ sở của cán bộ (ví dụ: "Cơ sở Hà Nội"). Không cho phép đổi sang cơ sở khác. |
| **Quản trị viên** (`ADMIN`) | **Toàn quyền:**<br>1. Xem **Toàn bộ trường** (Mặc định `GLOBAL`, tổng hợp cả 5 cơ sở + có Bảng So sánh 5 cơ sở).<br>2. Chọn xem **Cụ thể 1 cơ sở** (`HAN`, `HCM`, `DAN`, `CAN`, `QNH`). | **Active (Cho phép chọn):**<br>- "Toàn trường (Tất cả cơ sở)" (`GLOBAL`)<br>- Hà Nội (`HAN`)<br>- TP. Hồ Chí Minh (`HCM`)<br>- Đà Nẵng (`DAN`)<br>- Cần Thơ (`CAN`)<br>- Quy Nhơn (`QNH`) |

> ⚠️ **Lưu ý bảo mật:** Backend sẽ kiểm tra chặt chẽ token JWT. Nếu CTSV cố tình gửi request với query param `campusCode` của cơ sở khác, Backend sẽ trả về HTTP `403 Forbidden`. Frontend cần xử lý hiển thị thông báo lỗi thân thiện nếu gặp mã lỗi này.

---

## 3. 📡 THÔNG TIN API CHI TIẾT

### URL & Method
```http
GET {{VITE_API_BASE_URL}}/api/v1/student-affairs/radar/overview
```

### Headers
```http
Authorization: Bearer <access_token>
Content-Type: application/json
```

### Query Parameters
| Tham số | Kiểu | Bắt buộc? | Mô tả |
| :--- | :---: | :---: | :--- |
| `semester` | `string` | Không | Mã học kỳ cần xem (ví dụ: `FA26`, `SP27`, `SU26`). Nếu bỏ trống, Backend tự động lấy học kỳ đang kích hoạt (`active`). |
| `campusCode` | `string` | Không | Mã cơ sở muốn lọc (`GLOBAL`, `HAN`, `HCM`, `DAN`, `CAN`, `QNH`). Dành riêng cho `ADMIN`. Đối với CTSV, Backend tự động áp dụng cơ sở của CTSV. |

---

## 4. 📦 RESPONSE JSON SCHEMA & GIẢI THÍCH TRƯỜNG DỮ LIỆU

```json
{
  "scope": "GLOBAL",
  "campusCode": "GLOBAL",
  "campusName": "Toàn trường",
  "semesterCode": "FA26",
  "academicYear": "2026-2027",
  "totalStudents": 150,
  "totalApprovedDeclarations": 420,
  "totalRawPointsAwarded": 28400.0,
  "averageD": 38.45,
  "averageJ": 0.725,
  "averageM": 1.12,
  "averageERI": 46.82,
  "pillars": [
    {
      "pillar": "Academic",
      "pillarName": "Học tập",
      "description": "Các hoạt động học thuật, chứng chỉ kỹ năng chuyên môn, hội thảo chuyên ngành và cuộc thi kiến thức.",
      "tau": 1000.0,
      "totalRawPoints": 6200.0,
      "averageRawPoints": 58.5,
      "averageSaturatedScore": 45.2,
      "studentCount": 106,
      "leaderCount": 12,
      "masteryTierCounts": {
        "Unexplored": 44,
        "Starter": 38,
        "Practitioner": 48,
        "Contributor": 16,
        "Leader": 4
      }
    }
  ],
  "profileTitlesDistribution": [
    { "title": "Người Toàn Diện", "count": 28, "percentage": 18.67 },
    { "title": "Thế mạnh Học tập", "count": 45, "percentage": 30.0 },
    { "title": "Chưa khám phá", "count": 77, "percentage": 51.33 }
  ],
  "campusesComparison": [
    {
      "campusCode": "HAN",
      "campusName": "Hà Nội (Hòa Lạc)",
      "totalStudents": 52,
      "totalApprovedDeclarations": 160,
      "averageD": 41.2,
      "averageJ": 0.74,
      "averageM": 1.15,
      "averageERI": 51.3,
      "topStrengthPillar": "Học tập"
    }
  ]
}
```

### Giải thích các nhóm chỉ số:
1. **Thông tin chung:**
   - `scope`: `"GLOBAL"` (toàn trường) hoặc `"CAMPUS"` (cụ thể 1 cơ sở).
   - `totalStudents`: Tổng số sinh viên có ít nhất 1 minh chứng được phê duyệt trong kỳ.
   - `totalApprovedDeclarations`: Tổng số hồ sơ minh chứng đã duyệt.
   - `totalRawPointsAwarded`: Tổng điểm thô đóng góp toàn trường/cơ sở.
2. **Bộ 4 chỉ số tổng hợp (Key Metrics):**
   - `averageD` (Độ sâu / Tích lũy trung bình): Thang 0 - 100, thể hiện mức độ dồi dào trải nghiệm.
   - `averageJ` (Độ cân bằng / Entropy): Thang 0 - 1, càng gần 1 càng phân bổ đồng đều giữa các khía cạnh.
   - `averageM` (Hệ số thực chiến): Thang 1.0 - 1.3, thưởng thêm khi sinh viên có tham gia dự án thực tế doanh nghiệp.
   - `averageERI` (Chỉ số Trải nghiệm Tổng hợp - Experience Radar Index): Thang 0 - 130, chỉ số vàng đo lường sự phát triển toàn diện của sinh viên.
3. **Mảng `pillars` (Chi tiết từng trục Radar):**
   - `pillarName`: Tên tiếng Việt hiển thị trên đồ thị Radar (`Học tập`, `Nghiên cứu`, `Quốc tế`, `Thể thao & văn hóa`, `Cộng đồng`, `Khởi nghiệp`, `Thực chiến dự án (+1)`).
   - `averageSaturatedScore`: Điểm bão hòa trung bình (0 - 100), dùng để vẽ các đỉnh của đa giác biểu đồ Radar!
   - `masteryTierCounts`: Số lượng sinh viên phân bố theo 5 cấp độ:
     - `Unexplored` (Chưa khám phá): Điểm = 0
     - `Starter` (Khởi đầu): 0 < Điểm < 20
     - `Practitioner` (Thực hành): 20 <= Điểm < 50
     - `Contributor` (Đóng góp): 50 <= Điểm < 80
     - `Leader` (Dẫn dắt): Điểm >= 80 và có chứng nhận Leader
4. **Mảng `profileTitlesDistribution`:**
   - Phân bố danh hiệu sinh viên ("Người Toàn Diện", "Thế mạnh Học tập", "Thế mạnh Nghiên cứu", v.v.). Thích hợp vẽ biểu đồ tròn (Donut / Pie Chart) hoặc Progress Bar.
5. **Mảng `campusesComparison` (Chỉ xuất hiện khi Admin xem Toàn trường):**
   - Bảng so sánh chỉ số ERI, số sinh viên, và trục thế mạnh của cả 5 cơ sở FPT.

---

## 5. 💻 TYPESCRIPT DEFINITIONS CHO FRONTEND

```typescript
export interface PillarOverview {
  pillar: string;
  pillarName: string;
  description: string;
  tau: number;
  totalRawPoints: number;
  averageRawPoints: number;
  averageSaturatedScore: number;
  studentCount: number;
  leaderCount: number;
  masteryTierCounts: Record<string, number>;
}

export interface ProfileTitleStat {
  title: string;
  count: number;
  percentage: number;
}

export interface CampusComparisonItem {
  campusCode: string;
  campusName: string;
  totalStudents: number;
  totalApprovedDeclarations: number;
  averageD: number;
  averageJ: number;
  averageM: number;
  averageERI: number;
  topStrengthPillar: string;
}

export interface CampusRadarOverviewResponse {
  scope: 'CAMPUS' | 'GLOBAL';
  campusCode: string;
  campusName: string;
  semesterCode: string;
  academicYear: string;
  totalStudents: number;
  totalApprovedDeclarations: number;
  totalRawPointsAwarded: number;
  averageD: number;
  averageJ: number;
  averageM: number;
  averageERI: number;
  pillars: PillarOverview[];
  profileTitlesDistribution: ProfileTitleStat[];
  campusesComparison?: CampusComparisonItem[] | null;
}
```

---

## 6. 🎨 HƯỚNG DẪN XÂY DỰNG GIAO DIỆN (UI/UX LAYOUT)

Giao diện được khuyến nghị bố trí gồm 4 khu vực chính:

```
+-----------------------------------------------------------------------------------+
|  HEADER: [Chọn Học kỳ: FA26 v]   [Chọn Cơ sở: Toàn trường v (Chỉ Admin)]           |
+-----------------------------------------------------------------------------------+
|  ROW 1: KPI CARDS (4 Thẻ chỉ số chính)                                            |
|  [ ERI Trung bình: 46.8 ] [ Độ sâu D: 38.5 ] [ Cân bằng J: 0.72 ] [ SV: 150 ]     |
+-----------------------------------------------------------------------------------+
|  ROW 2: BIỂU ĐỒ TRỌNG TÂM                                                         |
|  [ CỘT TRÁI: Ô BIỂU ĐỒ RADAR 6+1 ]       | [ CỘT PHẢI: PHÂN BỐ DANH HIỆU & TIERS ]|
|  - Vẽ 6 đỉnh trục tương ứng 6 lĩnh vực   | - Donut chart tỷ lệ danh hiệu         |
|  - Giá trị vẽ = averageSaturatedScore    | - Bảng xếp hạng trục có SV tham gia   |
|  - Hover tooltip hiển thị mô tả & số SV  |                                        |
+-----------------------------------------------------------------------------------+
|  ROW 3: SO SÁNH 5 CƠ SỞ (Chỉ hiển thị khi Admin xem Toàn trường GLOBAL)           |
|  Bảng bảng số liệu: Cơ sở | Số SV | ERI TB | Độ sâu D | Cân bằng J | Thế mạnh      |
+-----------------------------------------------------------------------------------+
```

---

## 7. 🚀 CODE MẪU SERVICE & REACT HOOK

### 7.1. API Service (`src/services/radarService.ts`)
```typescript
import api from './api';
import { CampusRadarOverviewResponse } from '../types/radar';

export const getRadarOverview = async (
  semester?: string,
  campusCode?: string
): Promise<CampusRadarOverviewResponse> => {
  const params: Record<string, string> = {};
  if (semester) params.semester = semester;
  if (campusCode) params.campusCode = campusCode;

  const response = await api.get<CampusRadarOverviewResponse>(
    '/api/v1/student-affairs/radar/overview',
    { params }
  );
  return response.data;
};
```

### 7.2. Radar Chart Component thuần SVG (`src/components/RadarChart.tsx`)
```tsx
import React from 'react';
import { PillarOverview } from '../types/radar';

interface RadarChartProps {
  pillars: PillarOverview[];
  size?: number;
}

export const RadarChart: React.FC<RadarChartProps> = ({ pillars, size = 360 }) => {
  // Lấy 6 trục chính để vẽ hình lục giác đều
  const corePillars = pillars.filter(p => p.pillar !== 'RealWorldWork').slice(0, 6);
  const center = size / 2;
  const radius = size * 0.38;
  const angleStep = (2 * Math.PI) / corePillars.length;

  const getCoordinates = (value: number, index: number, maxVal = 100) => {
    const angle = index * angleStep - Math.PI / 2;
    const r = (value / maxVal) * radius;
    return {
      x: center + r * Math.cos(angle),
      y: center + r * Math.sin(angle),
    };
  };

  // Tạo chuỗi điểm polygon cho dữ liệu thực tế
  const polygonPoints = corePillars
    .map((p, i) => {
      const { x, y } = getCoordinates(p.averageSaturatedScore, i);
      return `${x},${y}`;
    })
    .join(' ');

  return (
    <div className="flex flex-col items-center">
      <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`}>
        {/* Vòng lưới đồng tâm 25%, 50%, 75%, 100% */}
        {[25, 50, 75, 100].map((level) => {
          const gridPoints = corePillars
            .map((_, i) => {
              const { x, y } = getCoordinates(level, i);
              return `${x},${y}`;
            })
            .join(' ');
          return (
            <polygon
              key={level}
              points={gridPoints}
              fill="none"
              stroke="#e2e8f0"
              strokeDasharray={level === 100 ? undefined : '3 3'}
              strokeWidth="1"
            />
          );
        })}

        {/* Các trục nan hoa nối từ tâm */}
        {corePillars.map((_, i) => {
          const { x, y } = getCoordinates(100, i);
          return (
            <line
              key={i}
              x1={center}
              y1={center}
              x2={x}
              y2={y}
              stroke="#cbd5e1"
              strokeWidth="1"
            />
          );
        })}

        {/* Vùng dữ liệu phủ màu cam FPT */}
        <polygon
          points={polygonPoints}
          fill="rgba(249, 115, 22, 0.25)"
          stroke="#ea580c"
          strokeWidth="2.5"
        />

        {/* Điểm nút & Nhãn trục */}
        {corePillars.map((p, i) => {
          const pt = getCoordinates(p.averageSaturatedScore, i);
          const labelPt = getCoordinates(118, i);
          return (
            <g key={p.pillar}>
              <circle cx={pt.x} cy={pt.y} r="4" fill="#ea580c" />
              <text
                x={labelPt.x}
                y={labelPt.y}
                textAnchor="middle"
                dominantBaseline="central"
                className="text-xs font-semibold fill-slate-700"
              >
                {p.pillarName}
              </text>
              <text
                x={labelPt.x}
                y={labelPt.y + 14}
                textAnchor="middle"
                dominantBaseline="central"
                className="text-[10px] fill-orange-600 font-bold"
              >
                {p.averageSaturatedScore.toFixed(1)}đ
              </text>
            </g>
          );
        })}
      </svg>
    </div>
  );
};
```
