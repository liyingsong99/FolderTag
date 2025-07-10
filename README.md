# Folder Tag

![image](https://github.com/user-attachments/assets/97b9d19c-e6a6-4485-abc5-64affee7c3ae)

Unity Editor Plugin that allows you to tag and annotate any folder and share it with other members of the project.

## Features

- **Easy-to-use**: Simply select any folder and add tags and descriptions in the Inspector panel
- **Team Collaboration**: Data is saved to ProjectSettings and can be shared via version control
- **Visual Organization**: Tags are displayed directly in the Project window for better project organization
- **Persistent Data**: All tag and description data is stored based on folder GUID in `ProjectSettings/FolderTag_Prefs.json`

## Installation

### Install via Unity Package Manager

1. Open Unity and go to **Window > Package Manager**
2. Click the **+** button in the top-left corner
3. Select **Add package from git URL**
4. Enter the following URL:
   ```
   https://github.com/liyingsong99/FolderTag.git
   ```
5. Click **Add**

### Install via Git URL (Alternative)

Add this line to your `Packages/manifest.json`:
```json
{
  "dependencies": {
    "com.liyingsong.foldertag": "https://github.com/liyingsong99/FolderTag.git"
  }
}
```

## Usage

1. **Adding Tags**: Select any folder in the Project window, then use the Inspector panel to add tags and descriptions
2. **Viewing Tags**: Tags will be displayed directly in the Project window next to folder names
3. **Team Sharing**: The `ProjectSettings/FolderTag_Prefs.json` file can be added to version control (Git/SVN) to share tags with team members

## Configuration

Access the Folder Tag settings through **Edit > Preferences > Folder Tag** to customize:
- Data save path
- Enable/disable scene tagging
- Show/hide gradient effects
- Configure sub-folder tinting
- Inspector editing options

## Data Storage

All tag and description data is stored in:
- **File**: `ProjectSettings/FolderTag_Prefs.json`
- **Format**: JSON with folder GUID as key
- **Version Control**: Safe to include in Git/SVN repositories

## Requirements

- Unity 2019.4 or later
- Compatible with all Unity platforms

## License

This project is licensed under the MIT License - see the [LICENSE.md](LICENSE.md) file for details.

## Contributing

Issues and pull requests are welcome! Please feel free to report bugs or suggest new features.

## Author

**Li Yingsong**
- GitHub: [@liyingsong99](https://github.com/liyingsong99)

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for a list of changes and version history. 