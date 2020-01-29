# HERAsharp

LINUX (Ubuntu) installation instructions:
As per https://docs.microsoft.com/en-us/dotnet/core/install/linux-package-manager-ubuntu-1904

### Step 1. Register Microsoft key and feed

wget -q https://packages.microsoft.com/config/ubuntu/19.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb

sudo dpkg -i packages-microsoft-prod.deb

### Step 2. Install the .NET Core SDK
sudo apt-get update

sudo apt-get install apt-transport-https

sudo apt-get update

sudo apt-get install dotnet-sdk-3.1

### Step 3. Build the solution
dotnet build code/herad.sln

### Step 4. Run the application and provide absolute path to the settings file
code/herad/bin/Debug/netcoreapp3.1/herad /mnt/c/git/HERAsharp/ec_test_settings.txt