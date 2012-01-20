﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using AvalonDock.Layout;

namespace AvalonDock.Controls
{
    public class LayoutAutoHideWindow : HwndHost, ILayoutControl
    {
        internal LayoutAutoHideWindow(LayoutAnchorControl anchor)
        {
            _model = anchor.Model as LayoutAnchorable;
            _side = (anchor.Model.Parent.Parent as LayoutAnchorSide).Side;
        }

        LayoutAnchorable _model;

        public ILayoutElement Model
        {
            get { return _model; }
        }

        HwndSource _internalHwndSource = null;

        protected override System.Runtime.InteropServices.HandleRef BuildWindowCore(System.Runtime.InteropServices.HandleRef hwndParent)
        {
            _internalHwndSource = new HwndSource(new HwndSourceParameters()
            {
                ParentWindow = hwndParent.Handle,
                WindowStyle = Win32Helper.WS_CHILD | Win32Helper.WS_VISIBLE | Win32Helper.WS_CLIPSIBLINGS | Win32Helper.WS_CLIPCHILDREN,
                Width = 1,
                Height = 1
            });

            CreateInternalGrid();
            _internalHwndSource.RootVisual = _internalGrid;


            return new HandleRef(this, _internalHwndSource.Handle);
        }

        protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32Helper.WM_WINDOWPOSCHANGING)
            {
                Win32Helper.SetWindowPos(_internalHwndSource.Handle, IntPtr.Zero, 0, 0, 0, 0, Win32Helper.SetWindowPosFlags.IgnoreMove | Win32Helper.SetWindowPosFlags.IgnoreResize);
            }

            return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        }

        protected override void DestroyWindowCore(System.Runtime.InteropServices.HandleRef hwnd)
        {
            Win32Helper.DestroyWindow(hwnd.Handle);
        }

        Grid _internalGrid = null;
        LayoutAnchorableControl _internalHost = null;
        AnchorSide _side;
        LayoutGridResizerControl _resizer = null;

        void CreateInternalGrid()
        {
            _internalGrid = new Grid();
            _internalGrid.SetBinding(Grid.BackgroundProperty, new Binding("DataContext.Background") { Source = _model.Root.Manager });

            _internalHost = new LayoutAnchorableControl() { DataContext = _model } ;
            _resizer = new LayoutGridResizerControl();

            _resizer.DragStarted += new System.Windows.Controls.Primitives.DragStartedEventHandler(OnResizerDragStarted);
            _resizer.DragDelta += new System.Windows.Controls.Primitives.DragDeltaEventHandler(OnResizerDragDelta);
            _resizer.DragCompleted += new System.Windows.Controls.Primitives.DragCompletedEventHandler(OnResizerDragCompleted);
            if (_side == AnchorSide.Right)
            {
                _internalGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
                _internalGrid.ColumnDefinitions.Add(new ColumnDefinition() { 
                    Width = _model.AutoHideWidth == 0.0 ? GridLength.Auto : new GridLength(_model.AutoHideWidth, GridUnitType.Pixel),
                    MinWidth = _model.AutoHideMinWidth
                    });

                _internalGrid.Children.Add(_resizer); Grid.SetColumn(_resizer, 0);
                _internalGrid.Children.Add(_internalHost); Grid.SetColumn(_internalHost, 1);
                
                _resizer.Cursor = Cursors.SizeWE;

                HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            }

            AddLogicalChild(_internalGrid);

        }

        void OnResizerDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            LayoutGridResizerControl splitter = sender as LayoutGridResizerControl;
            var rootVisual = this.FindVisualTreeRoot() as Visual;

            var trToWnd = TransformToAncestor(rootVisual);
            Vector transformedDelta = trToWnd.Transform(new Point(e.HorizontalChange, e.VerticalChange)) -
                trToWnd.Transform(new Point());

            double delta;
            if (_side == AnchorSide.Right || _side == AnchorSide.Left)
                delta = Canvas.GetLeft(_resizerGhost) - _initialStartPoint.X;
            else
                delta = Canvas.GetTop(_resizerGhost) - _initialStartPoint.Y;

            if (_side == AnchorSide.Right)
            {
                if (_model.AutoHideWidth == 0.0)
                    _model.AutoHideWidth = _internalHost.ActualWidth - delta;
                else
                    _model.AutoHideWidth -= delta;

                _internalGrid.ColumnDefinitions[1].Width = new GridLength(_model.AutoHideWidth, GridUnitType.Pixel);
            }


            HideResizerOverlayWindow();

            InvalidateMeasure();
        }

        void OnResizerDragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            LayoutGridResizerControl splitter = sender as LayoutGridResizerControl;
            var rootVisual = this.FindVisualTreeRoot() as Visual;

            var trToWnd = TransformToAncestor(rootVisual);
            Vector transformedDelta = trToWnd.Transform(new Point(e.HorizontalChange, e.VerticalChange)) -
                trToWnd.Transform(new Point());

            if (_side == AnchorSide.Right || _side == AnchorSide.Left)
            {
                Canvas.SetLeft(_resizerGhost, MathHelper.MinMax(_initialStartPoint.X + transformedDelta.X, 0.0, _resizerWindowHost.Width - _resizerGhost.Width));
            }
            else
            {
                Canvas.SetTop(_resizerGhost, MathHelper.MinMax(_initialStartPoint.Y + transformedDelta.Y, 0.0, _resizerWindowHost.Height - _resizerGhost.Height));
            }
        }

        void OnResizerDragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            var resizer = sender as LayoutGridResizerControl;
            ShowResizerOverlayWindow(resizer);
        }

        protected override System.Collections.IEnumerator LogicalChildren
        {
            get
            {
                return new UIElement[] { _internalGrid }.GetEnumerator();
            }
        }

        Border _resizerGhost = null;
        Window _resizerWindowHost = null;
        Vector _initialStartPoint;

        void ShowResizerOverlayWindow(LayoutGridResizerControl splitter)
        {
            _resizerGhost = new Border()
            {
                Background = splitter.BackgroundWhileDragging,
                Opacity = splitter.OpacityWhileDragging
            };

            var parentManager = Parent as DockingManager;
            var modelControlActualSize = this._internalHost.TransformActualSizeToAncestor();

            Point ptTopLeftScreen = parentManager.GetAutoHideAreaElement().PointToScreenDPI(new Point());

            var managerSize = parentManager.GetAutoHideAreaElement().TransformActualSizeToAncestor();

            Size windowSize;

            if (_side == AnchorSide.Right || _side == AnchorSide.Left)
            {
                windowSize = new Size(
                    managerSize.Width - _model.AutoHideMinWidth - 25.0 + splitter.ActualWidth,
                    managerSize.Height);

                _resizerGhost.Width = splitter.ActualWidth;
                _resizerGhost.Height = windowSize.Height;
                ptTopLeftScreen.Offset(25, 0.0);
            }
            else
            {
                windowSize = new Size(
                    managerSize.Width,
                    managerSize.Height - _model.AutoHideMinHeight - 25.0 + splitter.ActualHeight);

                _resizerGhost.Height = splitter.ActualHeight;
                _resizerGhost.Width = windowSize.Width;
                ptTopLeftScreen.Offset(0.0, 25.0);
            }

            _initialStartPoint = splitter.PointToScreenDPI(new Point()) - ptTopLeftScreen;

            if (_side == AnchorSide.Right || _side == AnchorSide.Left)
            {
                Canvas.SetLeft(_resizerGhost, _initialStartPoint.X);
            }
            else
            {
                Canvas.SetTop(_resizerGhost, _initialStartPoint.Y);
            }

            Canvas panelHostResizer = new Canvas()
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch
            };

            panelHostResizer.Children.Add(_resizerGhost);


            _resizerWindowHost = new Window()
            {
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = System.Windows.WindowStyle.None,
                ShowInTaskbar = false,
                AllowsTransparency = true,
                Background = null,
                Width = windowSize.Width,
                Height = windowSize.Height,
                Left = ptTopLeftScreen.X,
                Top = ptTopLeftScreen.Y,
                ShowActivated = false,
                Owner = Window.GetWindow(this),
                Content = panelHostResizer
            };

            _resizerWindowHost.Show();
        }

        void HideResizerOverlayWindow()
        {
            if (_resizerWindowHost != null)
            {
                _resizerWindowHost.Close();
                _resizerWindowHost = null;
            }
        }

        protected override Size MeasureOverride(Size constraint)
        {
            //return base.MeasureOverride(constraint);
            _internalGrid.Measure(constraint);
            return _internalGrid.DesiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            //return base.ArrangeOverride(finalSize);
            _internalGrid.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }
}
