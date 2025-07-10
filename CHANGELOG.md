# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.1] - 2025-01-27

### Fixed
- **Memory Leak**: Fixed Texture2D memory leak in gradient generation system
- **Thread Safety**: Added thread-safe locking mechanism for static data access
- **Exception Handling**: Enhanced file I/O and JSON serialization error handling
- **Performance**: Implemented caching system to reduce GUI redraw overhead
- **Input Validation**: Improved tag length validation with control character filtering
- **Null Safety**: Added comprehensive null checks and defensive programming

### Changed
- **Code Architecture**: Refactored Inspector classes with new BaseInspector base class
- **Error Logging**: Enhanced error messages with detailed context information
- **Performance**: Optimized dictionary lookup operations with smart caching
- **Code Quality**: Added comprehensive XML documentation comments
- **Cache Management**: Automatic cache invalidation when data changes

### Technical Improvements
- Thread-safe data access with lock mechanism
- Smart GUI caching to prevent redundant operations
- Improved exception handling for edge cases
- Enhanced input validation and sanitization
- Better resource management and cleanup

## [1.0.0] - 2025-01-27

### Added
- Initial release as Unity Package Manager (UPM) compatible package
- Folder tagging and annotation functionality
- Inspector panel integration for easy tag editing
- Visual display of tags in Project window
- Settings panel in Edit > Preferences > Folder Tag
- Support for team collaboration via ProjectSettings/FolderTag_Prefs.json
- Gradient effects and visual customization options
- Scene tagging support
- Sub-folder tinting configuration

### Changed
- Restructured project to follow UPM package conventions
- Updated assembly definition files to use proper UPM naming
- Improved documentation and installation instructions

### Technical Details
- Compatible with Unity 2019.4 and later
- All data stored in ProjectSettings/FolderTag_Prefs.json
- Editor-only functionality (no runtime dependencies)
- MIT License

## [Unreleased]

### Planned
- Additional customization options
- Performance optimizations
- Enhanced UI/UX improvements

---

## Version History

This package was originally developed as a Unity Asset Store submission but was later open-sourced and converted to UPM format for easier distribution and community collaboration. 