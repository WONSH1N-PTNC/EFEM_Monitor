using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Esam.Hmi.Controls
{
    /// <summary>트렌드 차트에 표시할 1개 채널.</summary>
    public sealed class TrendSeries
    {
        /// <summary>범례 표시명(예: "2-1").</summary>
        public string Label { get; set; }

        /// <summary>선 색상.</summary>
        public Brush Stroke { get; set; }

        /// <summary>시계열 값. 인덱스 0 이 가장 오래된 표본이다.</summary>
        public IList<double> Values { get; set; }

        /// <summary>채널을 생성한다.</summary>
        public TrendSeries()
        {
            Values = new List<double>();
        }
    }

    /// <summary>트렌드 차트에 표시할 알람 이벤트 마커.</summary>
    public sealed class TrendMarker
    {
        /// <summary>알람 코드(예: "A03").</summary>
        public string Code { get; set; }

        /// <summary>가로 위치 비율(0~1). 0 이 가장 오래된 시점이다.</summary>
        public double Position { get; set; }

        /// <summary>마커 색상.</summary>
        public Brush Stroke { get; set; }
    }

    /// <summary>
    /// 다중 채널 트렌드 차트. 디자인 원안의 SVG polyline 트렌드를 WPF 로 옮긴 것이다.
    /// </summary>
    /// <remarks>
    /// <para>ScottPlot 같은 외부 차트 라이브러리를 쓰지 않은 이유는 두 가지다.
    /// 첫째, 이 화면이 요구하는 것은 5채널 × 120표본의 단순 선 그래프이므로
    /// 직접 그리는 편이 의존성 없이 가볍다. 둘째, 알람 발생 시점을 수직 마커로 겹쳐
    /// 그려야 하는데 이 오버레이가 디자인의 핵심 요소다.</para>
    /// <para>Phase 5 현장 튜닝에서 원본 해상도 로그를 장시간 구간까지 스크롤·확대해야 할 때가 오면
    /// 그때 ScottPlot 으로 교체한다. 지금은 실시간 감시용 요약 트렌드에 집중한다.</para>
    /// <para>정상 대역을 반투명 띠로 깔아 두므로, 선이 띠를 벗어나는 순간이
    /// 곧 이탈 시점이 되어 눈으로 바로 확인된다.</para>
    /// </remarks>
    public class TrendChart : FrameworkElement
    {
        /// <summary>좌측 눈금 라벨이 차지하는 폭.</summary>
        private const double AxisWidth = 46.0;

        #region 의존 속성

        /// <summary>표시할 채널 목록.</summary>
        public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
            "Series", typeof(IEnumerable<TrendSeries>), typeof(TrendChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>알람 이벤트 마커 목록.</summary>
        public static readonly DependencyProperty MarkersProperty = DependencyProperty.Register(
            "Markers", typeof(IEnumerable<TrendMarker>), typeof(TrendChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>Y 축 하한.</summary>
        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
            "Minimum", typeof(double), typeof(TrendChart),
            new FrameworkPropertyMetadata(-40.0, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>Y 축 상한.</summary>
        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            "Maximum", typeof(double), typeof(TrendChart),
            new FrameworkPropertyMetadata(47.0, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>정상 대역 하한.</summary>
        public static readonly DependencyProperty BandLowProperty = DependencyProperty.Register(
            "BandLow", typeof(double), typeof(TrendChart),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>정상 대역 상한.</summary>
        public static readonly DependencyProperty BandHighProperty = DependencyProperty.Register(
            "BandHigh", typeof(double), typeof(TrendChart),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>목표값. 대역 안에 파선으로 표시한다.</summary>
        public static readonly DependencyProperty SetpointProperty = DependencyProperty.Register(
            "Setpoint", typeof(double), typeof(TrendChart),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>정상 대역 음영 색상.</summary>
        public static readonly DependencyProperty BandBrushProperty = DependencyProperty.Register(
            "BandBrush", typeof(Brush), typeof(TrendChart),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>격자선 색상.</summary>
        public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
            "GridBrush", typeof(Brush), typeof(TrendChart),
            new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>눈금 라벨 색상.</summary>
        public static readonly DependencyProperty AxisBrushProperty = DependencyProperty.Register(
            "AxisBrush", typeof(Brush), typeof(TrendChart),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>플롯 영역 배경색.</summary>
        public static readonly DependencyProperty PlotBackgroundProperty = DependencyProperty.Register(
            "PlotBackground", typeof(Brush), typeof(TrendChart),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>Y 축 라벨 소수점 자리수.</summary>
        public static readonly DependencyProperty DecimalsProperty = DependencyProperty.Register(
            "Decimals", typeof(int), typeof(TrendChart),
            new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// 재렌더링 트리거. 값이 바뀔 때마다 차트를 다시 그린다.
        /// </summary>
        /// <remarks>
        /// <para><b>왜 필요한가</b>: <see cref="Series"/> 는 참조 타입 의존 속성이다.
        /// WPF 는 참조 타입 DP 의 변경 여부를 <b>참조 동등성</b>으로 판정하므로,
        /// ViewModel 이 같은 컬렉션 인스턴스의 내용만 바꾸면(<c>List.Clear()/AddRange()</c>)
        /// DP 값이 "변하지 않았다"고 판단되어 <see cref="FrameworkPropertyMetadataOptions.AffectsRender"/>
        /// 가 발동하지 않는다. 결과적으로 트렌드가 첫 화면에서 멈춘다.</para>
        /// <para>ViewModel 이 갱신마다 이 값을 1 증가시키면 값 타입(long) 비교가 성립해
        /// 확실하게 재렌더링된다. 컬렉션을 매번 새로 만들어 GC 를 늘리는 것보다 가볍다.</para>
        /// </remarks>
        public static readonly DependencyProperty RevisionProperty = DependencyProperty.Register(
            "Revision", typeof(long), typeof(TrendChart),
            new FrameworkPropertyMetadata(0L, FrameworkPropertyMetadataOptions.AffectsRender));

        #endregion

        #region 속성 래퍼

        /// <summary>표시할 채널 목록.</summary>
        public IEnumerable<TrendSeries> Series
        {
            get { return (IEnumerable<TrendSeries>)GetValue(SeriesProperty); }
            set { SetValue(SeriesProperty, value); }
        }

        /// <summary>알람 이벤트 마커 목록.</summary>
        public IEnumerable<TrendMarker> Markers
        {
            get { return (IEnumerable<TrendMarker>)GetValue(MarkersProperty); }
            set { SetValue(MarkersProperty, value); }
        }

        /// <summary>Y 축 하한.</summary>
        public double Minimum
        {
            get { return (double)GetValue(MinimumProperty); }
            set { SetValue(MinimumProperty, value); }
        }

        /// <summary>Y 축 상한.</summary>
        public double Maximum
        {
            get { return (double)GetValue(MaximumProperty); }
            set { SetValue(MaximumProperty, value); }
        }

        /// <summary>정상 대역 하한.</summary>
        public double BandLow
        {
            get { return (double)GetValue(BandLowProperty); }
            set { SetValue(BandLowProperty, value); }
        }

        /// <summary>정상 대역 상한.</summary>
        public double BandHigh
        {
            get { return (double)GetValue(BandHighProperty); }
            set { SetValue(BandHighProperty, value); }
        }

        /// <summary>목표값.</summary>
        public double Setpoint
        {
            get { return (double)GetValue(SetpointProperty); }
            set { SetValue(SetpointProperty, value); }
        }

        /// <summary>정상 대역 음영 색상.</summary>
        public Brush BandBrush
        {
            get { return (Brush)GetValue(BandBrushProperty); }
            set { SetValue(BandBrushProperty, value); }
        }

        /// <summary>격자선 색상.</summary>
        public Brush GridBrush
        {
            get { return (Brush)GetValue(GridBrushProperty); }
            set { SetValue(GridBrushProperty, value); }
        }

        /// <summary>눈금 라벨 색상.</summary>
        public Brush AxisBrush
        {
            get { return (Brush)GetValue(AxisBrushProperty); }
            set { SetValue(AxisBrushProperty, value); }
        }

        /// <summary>플롯 영역 배경색.</summary>
        public Brush PlotBackground
        {
            get { return (Brush)GetValue(PlotBackgroundProperty); }
            set { SetValue(PlotBackgroundProperty, value); }
        }

        /// <summary>Y 축 라벨 소수점 자리수.</summary>
        public int Decimals
        {
            get { return (int)GetValue(DecimalsProperty); }
            set { SetValue(DecimalsProperty, value); }
        }

        /// <summary>재렌더링 트리거. ViewModel 이 갱신마다 증가시킨다.</summary>
        public long Revision
        {
            get { return (long)GetValue(RevisionProperty); }
            set { SetValue(RevisionProperty, value); }
        }

        #endregion

        /// <inheritdoc />
        protected override void OnRender(DrawingContext drawingContext)
        {
            if (drawingContext == null || ActualWidth <= AxisWidth + 10.0 || ActualHeight <= 20.0)
            {
                return;
            }

            double plotLeft = AxisWidth;
            double plotWidth = ActualWidth - AxisWidth;
            double plotHeight = ActualHeight;

            Rect plot = new Rect(plotLeft, 0.0, plotWidth, plotHeight);

            double min = Minimum;
            double max = Maximum;

            if (Math.Abs(max - min) < 1e-9)
            {
                return;
            }

            drawingContext.DrawRectangle(PlotBackground, null, plot);

            // 플롯 영역을 벗어나는 선을 자른다. 값이 축 범위를 넘어도 레이아웃이 깨지지 않아야 한다.
            drawingContext.PushClip(new RectangleGeometry(plot));

            DrawBand(drawingContext, plot, min, max);
            DrawGrid(drawingContext, plot, min, max);
            DrawSeries(drawingContext, plot, min, max);
            DrawMarkers(drawingContext, plot);

            drawingContext.Pop();

            DrawAxisLabels(drawingContext, plot, min, max);
        }

        /// <summary>정상 대역 음영과 목표값 파선을 그린다.</summary>
        private void DrawBand(DrawingContext dc, Rect plot, double min, double max)
        {
            if (BandBrush == null || Math.Abs(BandHigh - BandLow) < 1e-9)
            {
                return;
            }

            double top = ValueToY(Math.Max(BandLow, BandHigh), plot, min, max);
            double bottom = ValueToY(Math.Min(BandLow, BandHigh), plot, min, max);

            if (bottom - top > 0.5)
            {
                dc.DrawRectangle(BandBrush, null,
                    new Rect(plot.Left, top, plot.Width, bottom - top));
            }

            // 목표값 파선. 대역 중앙이 어디인지 알려 준다.
            double spY = ValueToY(Setpoint, plot, min, max);

            if (spY >= plot.Top && spY <= plot.Bottom)
            {
                Pen pen = new Pen(GridBrush, 1.0);
                pen.DashStyle = new DashStyle(new double[] { 4.0, 4.0 }, 0.0);
                pen.Freeze();

                dc.DrawLine(pen, new Point(plot.Left, spY), new Point(plot.Right, spY));
            }
        }

        /// <summary>수평 격자선을 그린다(5등분).</summary>
        private void DrawGrid(DrawingContext dc, Rect plot, double min, double max)
        {
            Pen pen = new Pen(GridBrush, 1.0);
            pen.Freeze();

            for (int i = 0; i <= 4; i++)
            {
                double y = plot.Top + (plot.Height * i / 4.0);

                // 상하 경계선은 반투명하게, 중간선은 더 흐리게 해 데이터가 묻히지 않게 한다.
                if (i == 0 || i == 4)
                {
                    dc.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
                }
            }
        }

        /// <summary>각 채널의 폴리라인을 그린다.</summary>
        private void DrawSeries(DrawingContext dc, Rect plot, double min, double max)
        {
            IEnumerable<TrendSeries> series = Series;
            if (series == null)
            {
                return;
            }

            foreach (TrendSeries item in series)
            {
                if (item == null || item.Values == null || item.Values.Count < 2)
                {
                    continue;
                }

                int count = item.Values.Count;
                double stepX = plot.Width / (count - 1);

                StreamGeometry geometry = new StreamGeometry();

                using (StreamGeometryContext ctx = geometry.Open())
                {
                    ctx.BeginFigure(
                        new Point(plot.Left, ValueToY(item.Values[0], plot, min, max)), false, false);

                    for (int i = 1; i < count; i++)
                    {
                        ctx.LineTo(
                            new Point(plot.Left + (stepX * i), ValueToY(item.Values[i], plot, min, max)),
                            true, false);
                    }
                }

                geometry.Freeze();

                Pen pen = new Pen(item.Stroke ?? Brushes.Gray, 1.4);
                pen.LineJoin = PenLineJoin.Round;
                pen.Freeze();

                dc.DrawGeometry(null, pen, geometry);
            }
        }

        /// <summary>알람 이벤트 수직 마커와 코드 라벨을 그린다.</summary>
        private void DrawMarkers(DrawingContext dc, Rect plot)
        {
            IEnumerable<TrendMarker> markers = Markers;
            if (markers == null)
            {
                return;
            }

            foreach (TrendMarker marker in markers)
            {
                if (marker == null)
                {
                    continue;
                }

                double x = plot.Left + (plot.Width * Clamp01(marker.Position));

                Pen pen = new Pen(marker.Stroke ?? Brushes.OrangeRed, 1.0);
                pen.Freeze();

                dc.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));

                if (string.IsNullOrEmpty(marker.Code))
                {
                    continue;
                }

                FormattedText label = new FormattedText(
                    marker.Code,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Consolas"),
                        FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    10.0,
                    marker.Stroke ?? Brushes.OrangeRed,
                    1.0);

                dc.DrawText(label, new Point(x + 4.0, plot.Top + 6.0));
            }
        }

        /// <summary>좌측 Y 축 눈금 라벨을 그린다.</summary>
        private void DrawAxisLabels(DrawingContext dc, Rect plot, double min, double max)
        {
            Typeface typeface = new Typeface(
                new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

            int decimals = Decimals < 0 ? 0 : Decimals;

            for (int i = 0; i <= 4; i++)
            {
                double value = max - ((max - min) * i / 4.0);
                double y = plot.Top + (plot.Height * i / 4.0);

                FormattedText text = new FormattedText(
                    value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture),
                        CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    9.0,
                    AxisBrush ?? Brushes.Gray,
                    1.0);

                // 첫 라벨과 마지막 라벨은 잘리지 않도록 위치를 안쪽으로 보정한다.
                double textY = y - (text.Height / 2.0);

                if (i == 0)
                {
                    textY = plot.Top;
                }
                else if (i == 4)
                {
                    textY = plot.Bottom - text.Height;
                }

                dc.DrawText(text, new Point(AxisWidth - text.Width - 8.0, textY));
            }
        }

        /// <summary>값을 플롯 영역의 Y 좌표로 변환한다.</summary>
        private static double ValueToY(double value, Rect plot, double min, double max)
        {
            double ratio = (value - min) / (max - min);

            if (double.IsNaN(ratio))
            {
                ratio = 0.0;
            }

            // Y 축은 위로 갈수록 값이 커지므로 비율을 뒤집는다.
            return plot.Bottom - (plot.Height * ratio);
        }

        /// <summary>값을 0~1 로 제한한다.</summary>
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
