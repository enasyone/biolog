import { PersonListView } from '../../features/identity/components/person-list-view';

export default function AlumniPage() {
  return (
    <PersonListView
      title="Alumni"
      description="Alumni roster, filtered from the People directory."
      category="alumni"
      requiredPermission="nav:alumni"
    />
  );
}
