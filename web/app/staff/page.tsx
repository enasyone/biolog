import { PersonListView } from '../../features/identity/components/person-list-view';

export default function StaffPage() {
  return (
    <PersonListView
      title="Staff"
      description="Staff roster, filtered from the People directory."
      category="staff"
      requiredPermission="nav:staff"
    />
  );
}
