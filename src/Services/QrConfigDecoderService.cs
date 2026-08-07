using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Nivan.Shield.Services
{
    public sealed class QrPayload
    {
        public string Text { get; set; }
        public string Format { get; set; }
    }

    /// <summary>
    /// Decodes QR images locally. ZXing.Net is embedded as a resource so config
    /// UUIDs and subscription addresses never leave the computer.
    /// </summary>
    public sealed class QrConfigDecoderService
    {
        private const long MaximumImageBytes = 12L * 1024L * 1024L;
        private const long MaximumPixels = 40L * 1000L * 1000L;
        private const int MaximumPayloadCharacters = 128 * 1024;
        private const string ResourceName = "Nivan.Shield.ZXing.dll";
        private Assembly _decoderAssembly;

        public IList<QrPayload> Decode(string imagePath)
        {
            ValidateImagePath(imagePath);
            using (Bitmap bitmap = LoadSafeBitmap(imagePath))
            {
                object reader = CreateReader();
                List<QrPayload> payloads = DecodeMultiple(reader, bitmap);
                if (payloads.Count == 0)
                {
                    object result = InvokeBitmapMethod(reader, "Decode", bitmap);
                    AddResult(payloads, result);
                }
                if (payloads.Count == 0)
                    throw new InvalidOperationException("No readable QR code was found in this image.");
                return payloads.Take(20).ToList();
            }
        }

        private object CreateReader()
        {
            Assembly assembly = LoadDecoderAssembly();
            Type type = assembly.GetType("ZXing.BarcodeReader", true, false);
            object reader = Activator.CreateInstance(type);
            SetBooleanProperty(reader, "AutoRotate", true);
            SetBooleanProperty(reader, "TryInverted", true);
            PropertyInfo optionsProperty = type.GetProperty("Options", BindingFlags.Instance | BindingFlags.Public);
            if (optionsProperty != null)
            {
                object options = optionsProperty.GetValue(reader, null);
                if (options != null) SetBooleanProperty(options, "TryHarder", true);
            }
            return reader;
        }

        private Assembly LoadDecoderAssembly()
        {
            if (_decoderAssembly != null) return _decoderAssembly;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException("The embedded offline QR decoder is missing. Rebuild the complete package.");
                if (stream.Length < 100 * 1024 || stream.Length > 2 * 1024 * 1024)
                    throw new InvalidOperationException("The embedded QR decoder has an unexpected size.");
                byte[] bytes = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0) throw new EndOfStreamException("The embedded QR decoder is incomplete.");
                    offset += read;
                }
                try { _decoderAssembly = Assembly.Load(bytes); }
                finally { Array.Clear(bytes, 0, bytes.Length); }
            }
            return _decoderAssembly;
        }

        private static List<QrPayload> DecodeMultiple(object reader, Bitmap bitmap)
        {
            List<QrPayload> payloads = new List<QrPayload>();
            object results = InvokeBitmapMethod(reader, "DecodeMultiple", bitmap);
            IEnumerable enumerable = results as IEnumerable;
            if (enumerable == null) return payloads;
            foreach (object result in enumerable)
            {
                AddResult(payloads, result);
                if (payloads.Count >= 20) break;
            }
            return payloads;
        }

        private static object InvokeBitmapMethod(object target, string methodName, Bitmap bitmap)
        {
            MethodInfo method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(delegate(MethodInfo candidate)
                {
                    ParameterInfo[] parameters = candidate.GetParameters();
                    return String.Equals(candidate.Name, methodName, StringComparison.Ordinal)
                        && parameters.Length == 1
                        && parameters[0].ParameterType.IsAssignableFrom(typeof(Bitmap));
                });
            return method == null ? null : method.Invoke(target, new object[] { bitmap });
        }

        private static void AddResult(ICollection<QrPayload> payloads, object result)
        {
            if (result == null) return;
            PropertyInfo textProperty = result.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public);
            PropertyInfo formatProperty = result.GetType().GetProperty("BarcodeFormat", BindingFlags.Instance | BindingFlags.Public);
            string text = Convert.ToString(textProperty == null ? null : textProperty.GetValue(result, null)).Trim();
            string format = Convert.ToString(formatProperty == null ? null : formatProperty.GetValue(result, null));
            if (!String.Equals(format, "QR_CODE", StringComparison.OrdinalIgnoreCase)) return;
            if (String.IsNullOrWhiteSpace(text)) return;
            if (text.Length > MaximumPayloadCharacters)
                throw new InvalidOperationException("The QR payload is too large to import safely.");
            if (payloads.Any(delegate(QrPayload item) { return String.Equals(item.Text, text, StringComparison.Ordinal); }))
                return;
            payloads.Add(new QrPayload { Text = text, Format = format });
        }

        private static Bitmap LoadSafeBitmap(string imagePath)
        {
            using (FileStream stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (Image image = Image.FromStream(stream, true, true))
            {
                long pixels = (long)image.Width * image.Height;
                if (image.Width < 32 || image.Height < 32 || pixels > MaximumPixels)
                    throw new InvalidOperationException("The QR image dimensions are outside the safe limit.");
                return new Bitmap(image);
            }
        }

        private static void ValidateImagePath(string imagePath)
        {
            if (String.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                throw new FileNotFoundException("Select an existing QR image file.", imagePath);
            FileInfo info = new FileInfo(imagePath);
            if (info.Length <= 0 || info.Length > MaximumImageBytes)
                throw new InvalidOperationException("The QR image must be smaller than 12 MB.");
            string extension = Path.GetExtension(imagePath).ToLowerInvariant();
            string[] allowed = new string[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };
            if (!allowed.Contains(extension))
                throw new InvalidOperationException("Use a PNG, JPG, BMP, GIF, or TIFF QR image.");
        }

        private static void SetBooleanProperty(object target, string propertyName, bool value)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.CanWrite && property.PropertyType == typeof(bool))
                property.SetValue(target, value, null);
        }
    }
}
