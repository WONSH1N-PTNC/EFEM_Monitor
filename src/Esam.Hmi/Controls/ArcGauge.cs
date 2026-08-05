using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Esam.Hmi.Controls
{
    /// <summary>
    /// 270° 원형 게이지. 디자인 원안의 SVG 게이지를 WPF 로 옮긴 것이다.
    /// </summary>
    /// <remarks>
    /// <para><b>기하 구조</b>: 반지름 50, 뷰박스 100×100 을 기준으로 하고 실제 크기에 맞춰 스케일한다.
    /// 원안의 상수 <c>ARC = 235.62</c>(둘레의 75%), <c>CIRC = 314.16</c>(2πr, r=50)에서
    /// 게이지가 원의 3/4, 즉 <b>270°</b> 를 차지함을 알 수 있다.</para>
    /// <para>시작 각은 좌하단(135°), 진행 방향은 시계방향이며 끝은 우하단(45°)이다.
    /// 아래쪽 90° 를 비워 두면 값 텍스트와 단위를 넣을 공간이 생기고,
    /// 0%와 100% 위치가 시각적으로 구분되어 오독을 줄인다.</para>
    /// <para><b>정상 대역 음영</b>: 차압 게이지는 목표 대역(Setpoint ± Band)을
    /// 반투명 아크로 겹쳐 그린다. 숫자를 읽지 않고도 "대역 안인지"를 즉시 알 수 있어야 한다.
    /// 이것이 이 화면의 핵심 요구사항이다.</para>
    /// <para>렌더링은 <see cref="OnRender"/> 에서 직접 수행한다.
    /// 게이지가 화면에 15개 이상 동시에 존재하고 200ms 주기로 갱신되므로,
    /// 시각 트리에 도형 요소를 쌓는 방식보다 직접 그리기가 훨씬 가볍다.</para>
    /// </remarks>
    public class ArcGauge : FrameworkElement
    {
        /// <summary>게이지가 차지하는 각도 [도]. 원안 기준 270°.</summary>
        public const double SweepDegrees = 270.0;

        /// <summary>시작 각 [도]. 화면 좌표계에서 좌하단(135°).</summary>
        public const double StartDegrees = 135.0;

        #region 의존 속성

        /// <summary>현재값을 0~1 로 정규화한 비율.</summary>
        public static readonly DependencyProperty RatioProperty = DependencyProperty.Register(
            "Ratio", typeof(double), typeof(ArcGauge),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>정상 대역 시작 비율(0~1). <see cref="ShowBand"/> 가 true 일 때만 쓰인다.</summary>
        public static readonly DependencyProperty BandStartProperty = DependencyProperty.Register(
            "BandStart", typeof(double), typeof(ArcGauge),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>정상 대역 끝 비율(0~1).</summary>
        public static readonly DependencyProperty BandEndProperty = DependencyProperty.Register(
            "BandEnd", typeof(double), typeof(ArcGauge),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>정상 대역 음영을 표시할지 여부.</summary>
        public static readonly DependencyProperty ShowBandProperty = DependencyProperty.Register(
            "ShowBand", typeof(bool), typeof(ArcGauge),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>값 아크 색상.</summary>
        public static readonly DependencyProperty ArcBrushProperty = DependencyProperty.Register(
            "ArcBrush", typeof(Brush), typeof(ArcGauge),
            new FrameworkPropertyMetadata(Brushes.LimeGreen, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>빈 구간(트랙) 색상.</summary>
        public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
            "TrackBrush", typeof(Brush), typeof(ArcGauge),
            new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>정상 대역 음영 색상.</summary>
        public static readonly DependencyProperty BandBrushProperty = DependencyProperty.Register(
            "BandBrush", typeof(Brush), typeof(ArcGauge),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>중앙에 표시할 값 문자열. 포맷은 ViewModel 이 결정한다.</summary>
        public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(
            "ValueText", typeof(string), typeof(ArcGauge),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>값 아래 표시할 단위 문자열(Pa, RPM, % OPEN 등).</summary>
        public static readonly DependencyProperty UnitTextProperty = DependencyProperty.Register(
            "UnitText", typeof(string), typeof(ArcGauge),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>값 텍스트 색상.</summary>
        public static readonly DependencyProperty ValueBrushProperty = DependencyProperty.Register(
            "ValueBrush", typeof(Brush), typeof(ArcGauge),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>단위 텍스트 색상.</summary>
        public static readonly DependencyProperty UnitBrushProperty = DependencyProperty.Register(
            "UnitBrush", typeof(Brush), typeof(ArcGauge),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>아크 두께. 뷰박스(100 기준) 단위이며 실제 크기에 맞춰 스케일된다.</summary>
        public static readonly DependencyProperty StrokeWidthProperty = DependencyProperty.Register(
            "StrokeWidth", typeof(double), typeof(ArcGauge),
            new FrameworkPropertyMetadata(7.0, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>값 텍스트 크기(실제 픽셀).</summary>
        public static readonly DependencyProperty ValueFontSizeProperty = DependencyProperty.Register(
            "ValueFontSize", typeof(double), typeof(ArcGauge),
            new FrameworkPropertyMetadata(24.0, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>값 텍스트 폰트.</summary>
        public static readonly DependencyProperty ValueFontFamilyProperty = DependencyProperty.Register(
            "ValueFontFamily", typeof(FontFamily), typeof(ArcGauge),
            new FrameworkPropertyMetadata(new FontFamily("Consolas"),
                FrameworkPropertyMetadataOptions.AffectsRender));

        #endregion

        #region 속성 래퍼

        /// <summary>현재값을 0~1 로 정규화한 비율.</summary>
        public double Ratio
        {
            get { return (double)GetValue(RatioProperty); }
            set { SetValue(RatioProperty, value); }
        }

        /// <summary>정상 대역 시작 비율(0~1).</summary>
        public double BandStart
        {
            get { return (double)GetValue(BandStartProperty); }
            set { SetValue(BandStartProperty, value); }
        }

        /// <summary>정상 대역 끝 비율(0~1).</summary>
        public double BandEnd
        {
            get { return (double)GetValue(BandEndProperty); }
            set { SetValue(BandEndProperty, value); }
        }

        /// <summary>정상 대역 음영 표시 여부.</summary>
        public bool ShowBand
        {
            get { return (bool)GetValue(ShowBandProperty); }
            set { SetValue(ShowBandProperty, value); }
        }

        /// <summary>값 아크 색상.</summary>
        public Brush ArcBrush
        {
            get { return (Brush)GetValue(ArcBrushProperty); }
            set { SetValue(ArcBrushProperty, value); }
        }

        /// <summary>빈 구간(트랙) 색상.</summary>
        public Brush TrackBrush
        {
            get { return (Brush)GetValue(TrackBrushProperty); }
            set { SetValue(TrackBrushProperty, value); }
        }

        /// <summary>정상 대역 음영 색상.</summary>
        public Brush BandBrush
        {
            get { return (Brush)GetValue(BandBrushProperty); }
            set { SetValue(BandBrushProperty, value); }
        }

        /// <summary>중앙 값 문자열.</summary>
        public string ValueText
        {
            get { return (string)GetValue(ValueTextProperty); }
            set { SetValue(ValueTextProperty, value); }
        }

        /// <summary>단위 문자열.</summary>
        public string UnitText
        {
            get { return (string)GetValue(UnitTextProperty); }
            set { SetValue(UnitTextProperty, value); }
        }

        /// <summary>값 텍스트 색상.</summary>
        public Brush ValueBrush
        {
            get { return (Brush)GetValue(ValueBrushProperty); }
            set { SetValue(ValueBrushProperty, value); }
        }

        /// <summary>단위 텍스트 색상.</summary>
        public Brush UnitBrush
        {
            get { return (Brush)GetValue(UnitBrushProperty); }
            set { SetValue(UnitBrushProperty, value); }
        }

        /// <summary>아크 두께(뷰박스 100 기준).</summary>
        public double StrokeWidth
        {
            get { return (double)GetValue(StrokeWidthProperty); }
            set { SetValue(StrokeWidthProperty, value); }
        }

        /// <summary>값 텍스트 크기.</summary>
        public double ValueFontSize
        {
            get { return (double)GetValue(ValueFontSizeProperty); }
            set { SetValue(ValueFontSizeProperty, value); }
        }

        /// <summary>값 텍스트 폰트.</summary>
        public FontFamily ValueFontFamily
        {
            get { return (FontFamily)GetValue(ValueFontFamilyProperty); }
            set { SetValue(ValueFontFamilyProperty, value); }
        }

        #endregion

        /// <inheritdoc />
        protected override void OnRender(DrawingContext drawingContext)
        {
            if (drawingContext == null)
            {
                return;
            }

            double side = Math.Min(ActualWidth, ActualHeight);
            if (side <= 1.0)
            {
                return;
            }

            // 뷰박스(100×100) → 실제 크기 스케일. 아크 두께도 함께 스케일해야 비율이 유지된다.
            double scale = side / 100.0;
            double stroke = StrokeWidth * scale;

            // 아크가 경계에서 잘리지 않도록 반지름에서 두께의 절반을 뺀다.
            double radius = (side / 2.0) - (stroke / 2.0) - (1.0 * scale);
            if (radius <= 0.0)
            {
                return;
            }

            Point center = new Point(ActualWidth / 2.0, ActualHeight / 2.0);

            // ── 1. 트랙(전체 270°) ───────────────────────────────────────────────
            DrawArc(drawingContext, center, radius, 0.0, 1.0, TrackBrush, stroke);

            // ── 2. 정상 대역 음영 ────────────────────────────────────────────────
            // 값 아크보다 먼저(아래에) 그려야 값이 대역에 가려지지 않는다.
            if (ShowBand)
            {
                double from = Clamp01(Math.Min(BandStart, BandEnd));
                double to = Clamp01(Math.Max(BandStart, BandEnd));

                if (to - from > 0.0005)
                {
                    DrawArc(drawingContext, center, radius, from, to, BandBrush, stroke);
                }
            }

            // ── 3. 값 아크 ───────────────────────────────────────────────────────
            double ratio = Clamp01(Ratio);
            if (ratio > 0.0005)
            {
                DrawArc(drawingContext, center, radius, 0.0, ratio, ArcBrush, stroke);
            }

            // ── 4. 중앙 텍스트 ───────────────────────────────────────────────────
            DrawCenterText(drawingContext, center, side);
        }

        /// <summary>
        /// 정규화 구간 [<paramref name="from"/>, <paramref name="to"/>] 에 해당하는 아크를 그린다.
        /// </summary>
        /// <param name="dc">드로잉 컨텍스트.</param>
        /// <param name="center">원 중심.</param>
        /// <param name="radius">반지름.</param>
        /// <param name="from">시작 비율(0~1).</param>
        /// <param name="to">끝 비율(0~1).</param>
        /// <param name="brush">선 색상.</param>
        /// <param name="thickness">선 두께.</param>
        private static void DrawArc(
            DrawingContext dc, Point center, double radius,
            double from, double to, Brush brush, double thickness)
        {
            if (brush == null || to <= from)
            {
                return;
            }

            double startAngle = StartDegrees + (from * SweepDegrees);
            double endAngle = StartDegrees + (to * SweepDegrees);
            double sweep = endAngle - startAngle;

            Point p0 = PointOnCircle(center, radius, startAngle);
            Point p1 = PointOnCircle(center, radius, endAngle);

            StreamGeometry geometry = new StreamGeometry();

            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(p0, false, false);

                // ArcTo 의 IsLargeArc 는 180° 초과 여부다. 270° 게이지에서는 반드시 판정해야
                // 한 바퀴 반대로 도는 도형이 나오지 않는다.
                ctx.ArcTo(
                    p1,
                    new Size(radius, radius),
                    0.0,
                    sweep > 180.0,
                    SweepDirection.Clockwise,
                    true,
                    false);
            }

            geometry.Freeze();

            Pen pen = new Pen(brush, thickness);
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;
            pen.Freeze();

            dc.DrawGeometry(null, pen, geometry);
        }

        /// <summary>중앙의 값·단위 텍스트를 그린다.</summary>
        /// <param name="dc">드로잉 컨텍스트.</param>
        /// <param name="center">원 중심.</param>
        /// <param name="side">게이지 한 변 길이.</param>
        private void DrawCenterText(DrawingContext dc, Point center, double side)
        {
            Typeface typeface = new Typeface(
                ValueFontFamily ?? new FontFamily("Consolas"),
                FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

            double valueSize = ValueFontSize > 0.0 ? ValueFontSize : side * 0.26;

            if (!string.IsNullOrEmpty(ValueText))
            {
                FormattedText value = CreateText(ValueText, typeface, valueSize, ValueBrush);

                // 단위가 있으면 값을 살짝 위로 올려 두 줄이 시각적 중심에 오게 한다.
                double offsetY = string.IsNullOrEmpty(UnitText) ? 0.0 : -side * 0.045;

                dc.DrawText(value, new Point(
                    center.X - (value.Width / 2.0),
                    center.Y - (value.Height / 2.0) + offsetY));
            }

            if (!string.IsNullOrEmpty(UnitText))
            {
                Typeface unitFace = new Typeface(
                    ValueFontFamily ?? new FontFamily("Consolas"),
                    FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

                FormattedText unit = CreateText(UnitText, unitFace, Math.Max(8.0, side * 0.10), UnitBrush);

                dc.DrawText(unit, new Point(
                    center.X - (unit.Width / 2.0),
                    center.Y + (side * 0.10)));
            }
        }

        /// <summary>렌더링용 <see cref="FormattedText"/> 를 만든다.</summary>
        /// <param name="text">문자열.</param>
        /// <param name="typeface">서체.</param>
        /// <param name="size">글자 크기.</param>
        /// <param name="brush">색상.</param>
        /// <returns>생성된 텍스트.</returns>
        private static FormattedText CreateText(string text, Typeface typeface, double size, Brush brush)
        {
            // net472 에서는 pixelsPerDip 인자를 받는 오버로드를 쓰는 것이 권장이지만,
            // 이 오버로드는 WPF 4.6.2 이후에만 존재하므로 4.7.2 에서 안전하게 사용할 수 있다.
            return new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                size,
                brush ?? Brushes.White,
                1.0);
        }

        /// <summary>지정 각도의 원주상 좌표를 구한다.</summary>
        /// <param name="center">원 중심.</param>
        /// <param name="radius">반지름.</param>
        /// <param name="degrees">각도 [도]. 0° 는 오른쪽, 시계방향으로 증가한다.</param>
        /// <returns>원주상 좌표.</returns>
        private static Point PointOnCircle(Point center, double radius, double degrees)
        {
            double radians = degrees * Math.PI / 180.0;

            // 화면 좌표계는 Y 축이 아래로 증가하므로, 각도를 그대로 쓰면 시계방향이 된다.
            return new Point(
                center.X + (radius * Math.Cos(radians)),
                center.Y + (radius * Math.Sin(radians)));
        }

        /// <summary>값을 0~1 로 제한한다.</summary>
        /// <param name="value">입력값.</param>
        /// <returns>제한된 값.</returns>
        private static double Clamp01(double value)
        {
            if (double.IsNaN(value) || value < 0.0)
            {
                return 0.0;
            }

            return value > 1.0 ? 1.0 : value;
        }
    }
}
