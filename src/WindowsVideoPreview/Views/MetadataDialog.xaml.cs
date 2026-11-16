using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WindowsVideoPreview.Models;
using WindowsVideoPreview.ViewModels;

namespace WindowsVideoPreview.Views;

public partial class MetadataDialog : Window
{
    public MetadataDialog(MediaMetadata? metaA, MediaMetadata? metaB)
    {
        InitializeComponent();
        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
        PopulatePanel(PanelA, metaA);
        if (metaB != null)
        {
            PopulatePanel(PanelB, metaB);
            TabB.Visibility = Visibility.Visible;
        }
        else
        {
            TabB.Visibility = Visibility.Collapsed;
        }
    }

    private void PopulatePanel(StackPanel panel, MediaMetadata? meta)
    {
        panel.Children.Clear();
        if (meta == null)
        {
            panel.Children.Add(new TextBlock 
            { 
                Text = "No metadata available for this video.", 
                Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
                Margin = new Thickness(0, 10, 0, 0)
            });
            return;
        }

        AddSection(panel, "General Info");
        AddRow(panel, "File Name:", meta.FileName);
        AddRow(panel, "File Path:", meta.FilePath);
        AddRow(panel, "File Size:", meta.FileSizeText);
        AddRow(panel, "Duration:", MainViewModel.FormatTimecode(meta.DurationSeconds));

        AddSection(panel, "Video Stream");
        AddRow(panel, "Resolution:", meta.Resolution);
        AddRow(panel, "Aspect Ratio:", meta.AspectRatio);
        AddRow(panel, "Codec:", meta.VideoCodec.ToUpper());
        AddRow(panel, "Pixel Format:", meta.PixelFormat);
        AddRow(panel, "Frame Rate (FPS):", meta.FpsText);
        AddRow(panel, "Total Frames:", meta.TotalFrames > 0 ? meta.TotalFrames.ToString("N0") : "N/A");
        if (meta.VideoBitrate > 0)
        {
            AddRow(panel, "Bitrate:", $"{meta.VideoBitrate / 1000} kbps");
        }

        AddSection(panel, "Audio Stream");
        if (meta.HasAudio)
        {
            AddRow(panel, "Audio Codec:", meta.AudioCodec.ToUpper());
            AddRow(panel, "Sample Rate:", $"{meta.SampleRate} Hz");
            AddRow(panel, "Channels:", meta.Channels == 2 ? "2 (Stereo)" : meta.Channels == 1 ? "1 (Mono)" : $"{meta.Channels} channels");
        }
        else
        {
            AddRow(panel, "Audio:", "No audio track detected in stream");
        }
    }

    private void AddSection(StackPanel panel, string title)
    {
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
            Margin = new Thickness(0, 14, 0, 6)
        });
        panel.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
            Margin = new Thickness(0, 0, 0, 8)
        });
    }

    private void AddRow(StackPanel panel, string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var lbl = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
            FontWeight = FontWeights.Medium
        };
        var val = new TextBlock
        {
            Text = value,
            Foreground = new SolidColorBrush(Color.FromRgb(244, 244, 245)),
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas, Segoe UI")
        };

        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(val, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(val);
        panel.Children.Add(grid);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
