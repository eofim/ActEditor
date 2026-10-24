using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GRF.FileFormats.SprFormat;
using GRF.Image;

namespace ActEditor.Tools.PaletteEditorTool {
	/// <summary>
	/// Interaction logic for Bgra32EditorDialog.xaml
	/// </summary>
	public partial class Bgra32EditorDialog : Window {
		private readonly Spr _targetSprite;
		private readonly Spr _originalSprite;
		private readonly ToneEditorMode _mode;
		private readonly List<int> _allImageIndexes;
		private readonly List<int> _selectedImageIndexes;
		private readonly List<int> _allPaletteIndexes;
		private readonly List<int> _selectedPaletteIndexes;
		private bool _isUpdating;
		private bool _accepted;

		public Bgra32EditorDialog(Spr targetSprite, IEnumerable<int> selectedImageIndexes, ToneEditorMode mode = ToneEditorMode.Bgra32) {
			InitializeComponent();

			_targetSprite = targetSprite;
			_originalSprite = new Spr(targetSprite);
			_mode = mode;
			GrfImageType imageType = mode == ToneEditorMode.Bgra32 ? GrfImageType.Bgra32 : GrfImageType.Indexed8;
			_allImageIndexes = Enumerable.Range(0, targetSprite.Images.Count)
				.Where(p => targetSprite.Images[p].GrfImageType == imageType)
				.ToList();
			_selectedImageIndexes = selectedImageIndexes == null
				? new List<int>()
				: selectedImageIndexes.Where(p => _allImageIndexes.Contains(p)).Distinct().OrderBy(p => p).ToList();
			_allPaletteIndexes = _getUsedPaletteIndexes(_allImageIndexes);
			_selectedPaletteIndexes = _getUsedPaletteIndexes(_selectedImageIndexes);

			_cbSelectedOnly.IsEnabled = _selectedImageIndexes.Count > 0;
			_cbSelectedOnly.IsChecked = _selectedImageIndexes.Count > 0;
			if (_mode == ToneEditorMode.Indexed8) {
				Title = "Indexed8 editor";
				_cbSelectedOnly.Content = "Colors used by selected layers only";
			}
			_targetColor.Color = Colors.LimeGreen;

			_updateLabels();

			Closing += delegate {
				if (!_accepted)
					RestoreOriginal();
			};
		}

		public Spr ResultSprite { get; private set; }
		public event Action PreviewChanged;

		private IEnumerable<int> _getTargetIndexes() {
			if (_cbSelectedOnly.IsEnabled && _cbSelectedOnly.IsChecked == true)
				return _selectedImageIndexes;

			return _allImageIndexes;
		}

		private IEnumerable<int> _getTargetPaletteIndexes() {
			if (_cbSelectedOnly.IsEnabled && _cbSelectedOnly.IsChecked == true)
				return _selectedPaletteIndexes;

			return _allPaletteIndexes;
		}

		private List<int> _getUsedPaletteIndexes(IEnumerable<int> imageIndexes) {
			if (_mode != ToneEditorMode.Indexed8)
				return new List<int>();

			return imageIndexes
				.SelectMany(index => _originalSprite.Images[index].Pixels.Select(pixel => (int)pixel))
				.Where(index => index != 0)
				.Distinct()
				.OrderBy(index => index)
				.ToList();
		}

		private void _control_ValueChanged(object sender, RoutedEventArgs e) {
			if (_isUpdating || !IsLoaded)
				return;

			_updateLabels();
			_applyPreview();
		}

		private void _targetColor_ColorChanged(object sender, Color color) {
			if (!_isUpdating && IsLoaded)
				_applyPreview();
		}

		private void _buttonReset_Click(object sender, RoutedEventArgs e) {
			_isUpdating = true;
			_sliderHue.Value = 0;
			_sliderSaturation.Value = 0;
			_sliderLightness.Value = 0;
			_sliderBrightness.Value = 0;
			_sliderContrast.Value = 0;
			_sliderTolerance.Value = 45;
			_sliderMinimumSaturation.Value = 10;
			_cbTargetColor.IsChecked = false;
			_targetColor.Color = Colors.LimeGreen;
			_isUpdating = false;

			_updateLabels();
			_applyPreview();
		}

		private void _buttonOk_Click(object sender, RoutedEventArgs e) {
			ResultSprite = new Spr(_targetSprite);
			_accepted = true;
			DialogResult = true;
		}

		private void _buttonCancel_Click(object sender, RoutedEventArgs e) {
			DialogResult = false;
		}

		public void RestoreOriginal() {
			for (int i = 0; i < _targetSprite.Images.Count; i++) {
				_targetSprite.Images[i] = _originalSprite.Images[i].Copy();
			}

			if (_targetSprite.Palette != null && _originalSprite.Palette != null)
				_targetSprite.Palette.SetPalette(_originalSprite.Palette.BytePalette);
		}

		private void _applyPreview() {
			var previewSprite = new Spr(_originalSprite);
			var options = new ToneOptions {
				Hue = _sliderHue.Value,
				Saturation = _sliderSaturation.Value / 100d,
				Lightness = _sliderLightness.Value / 100d,
				Brightness = _sliderBrightness.Value / 100d,
				Contrast = _sliderContrast.Value / 100d,
				UseTargetColor = _cbTargetColor.IsChecked == true,
				TargetColor = _targetColor.Color,
				HueTolerance = _sliderTolerance.Value / 360d,
				MinimumSaturation = _sliderMinimumSaturation.Value / 100d
			};

			if (_mode == ToneEditorMode.Bgra32) {
				foreach (int index in _getTargetIndexes()) {
					ApplyTone(previewSprite.Images[index], options);
				}
			}
			else if (previewSprite.Palette != null) {
				ApplyToneToPalette(previewSprite.Palette.BytePalette, _getTargetPaletteIndexes(), options);
			}

			for (int i = 0; i < _targetSprite.Images.Count; i++) {
				_targetSprite.Images[i] = previewSprite.Images[i];
			}

			if (_mode == ToneEditorMode.Indexed8 && _targetSprite.Palette != null && previewSprite.Palette != null)
				_targetSprite.Palette.SetPalette(previewSprite.Palette.BytePalette);

			if (_targetSprite.Palette != null)
				_targetSprite.Palette.OnPaletteChanged();

			PreviewChanged?.Invoke();
		}

		private void _updateLabels() {
			_tbHue.Text = ((int)Math.Round(_sliderHue.Value)).ToString();
			_tbSaturation.Text = ((int)Math.Round(_sliderSaturation.Value)).ToString() + "%";
			_tbLightness.Text = ((int)Math.Round(_sliderLightness.Value)).ToString() + "%";
			_tbBrightness.Text = ((int)Math.Round(_sliderBrightness.Value)).ToString() + "%";
			_tbContrast.Text = ((int)Math.Round(_sliderContrast.Value)).ToString() + "%";
			_tbTolerance.Text = ((int)Math.Round(_sliderTolerance.Value)).ToString() + " deg";
			_tbMinimumSaturation.Text = ((int)Math.Round(_sliderMinimumSaturation.Value)).ToString() + "%";
		}

		public static void ApplyTone(GrfImage image, ToneOptions options) {
			if (image == null || image.GrfImageType != GrfImageType.Bgra32)
				return;

			byte[] pixels = image.Pixels;
			double targetHue = 0d;

			if (options.UseTargetColor) {
				double ignoredSaturation;
				double ignoredLightness;
				_rgbToHsl(options.TargetColor.R / 255d, options.TargetColor.G / 255d, options.TargetColor.B / 255d, out targetHue, out ignoredSaturation, out ignoredLightness);
			}

			for (int i = 0; i + 3 < pixels.Length; i += 4) {
				byte alpha = pixels[i + 3];

				if (alpha == 0)
					continue;

				double b = pixels[i] / 255d;
				double g = pixels[i + 1] / 255d;
				double r = pixels[i + 2] / 255d;

				double h, s, l;
				_rgbToHsl(r, g, b, out h, out s, out l);

				if (options.UseTargetColor && !_matchesTargetColor(h, s, targetHue, options.HueTolerance, options.MinimumSaturation))
					continue;

				h = _wrapHue(h + options.Hue / 360d);
				s = _clamp01(s + options.Saturation);
				l = _clamp01(l + options.Lightness);

				_hslToRgb(h, s, l, out r, out g, out b);

				if (Math.Abs(options.Contrast) > double.Epsilon) {
					double factor = 1d + options.Contrast;
					r = _clamp01((r - 0.5d) * factor + 0.5d);
					g = _clamp01((g - 0.5d) * factor + 0.5d);
					b = _clamp01((b - 0.5d) * factor + 0.5d);
				}

				if (Math.Abs(options.Brightness) > double.Epsilon) {
					r = _clamp01(r + options.Brightness);
					g = _clamp01(g + options.Brightness);
					b = _clamp01(b + options.Brightness);
				}

				pixels[i] = (byte)Math.Round(b * 255d);
				pixels[i + 1] = (byte)Math.Round(g * 255d);
				pixels[i + 2] = (byte)Math.Round(r * 255d);
				pixels[i + 3] = alpha;
			}
		}

		public static void ApplyToneToPalette(byte[] palette, IEnumerable<int> paletteIndexes, ToneOptions options) {
			if (palette == null || paletteIndexes == null)
				return;

			foreach (int paletteIndex in paletteIndexes) {
				int offset = paletteIndex * 4;

				if (offset < 0 || offset + 3 >= palette.Length || palette[offset + 3] == 0)
					continue;

				// Indexed8 palettes are RGBA while Bgra32 pixels are BGRA.
				var pixel = new GrfImage(new[] { palette[offset + 2], palette[offset + 1], palette[offset], palette[offset + 3] }, 1, 1, GrfImageType.Bgra32);
				ApplyTone(pixel, options);
				palette[offset] = pixel.Pixels[2];
				palette[offset + 1] = pixel.Pixels[1];
				palette[offset + 2] = pixel.Pixels[0];
			}
		}

		private static bool _matchesTargetColor(double hue, double saturation, double targetHue, double hueTolerance, double minimumSaturation) {
			if (saturation < minimumSaturation)
				return false;

			double distance = Math.Abs(hue - targetHue);
			distance = Math.Min(distance, 1d - distance);
			return distance <= hueTolerance;
		}

		private static void _rgbToHsl(double r, double g, double b, out double h, out double s, out double l) {
			double max = Math.Max(r, Math.Max(g, b));
			double min = Math.Min(r, Math.Min(g, b));

			h = 0d;
			s = 0d;
			l = (max + min) / 2d;

			if (Math.Abs(max - min) < double.Epsilon)
				return;

			double d = max - min;
			s = l > 0.5d ? d / (2d - max - min) : d / (max + min);

			if (Math.Abs(max - r) < double.Epsilon)
				h = (g - b) / d + (g < b ? 6d : 0d);
			else if (Math.Abs(max - g) < double.Epsilon)
				h = (b - r) / d + 2d;
			else
				h = (r - g) / d + 4d;

			h /= 6d;
		}

		private static void _hslToRgb(double h, double s, double l, out double r, out double g, out double b) {
			if (Math.Abs(s) < double.Epsilon) {
				r = l;
				g = l;
				b = l;
				return;
			}

			double q = l < 0.5d ? l * (1d + s) : l + s - l * s;
			double p = 2d * l - q;

			r = _hueToRgb(p, q, h + 1d / 3d);
			g = _hueToRgb(p, q, h);
			b = _hueToRgb(p, q, h - 1d / 3d);
		}

		private static double _hueToRgb(double p, double q, double t) {
			if (t < 0d)
				t += 1d;
			if (t > 1d)
				t -= 1d;
			if (t < 1d / 6d)
				return p + (q - p) * 6d * t;
			if (t < 1d / 2d)
				return q;
			if (t < 2d / 3d)
				return p + (q - p) * (2d / 3d - t) * 6d;

			return p;
		}

		private static double _wrapHue(double value) {
			value %= 1d;

			if (value < 0d)
				value += 1d;

			return value;
		}

		private static double _clamp01(double value) {
			if (value < 0d)
				return 0d;
			if (value > 1d)
				return 1d;

			return value;
		}
	}

	public enum ToneEditorMode {
		Bgra32,
		Indexed8
	}

	public class ToneOptions {
		public double Hue { get; set; }
		public double Saturation { get; set; }
		public double Lightness { get; set; }
		public double Brightness { get; set; }
		public double Contrast { get; set; }
		public bool UseTargetColor { get; set; }
		public Color TargetColor { get; set; }
		public double HueTolerance { get; set; }
		public double MinimumSaturation { get; set; }
	}
}
